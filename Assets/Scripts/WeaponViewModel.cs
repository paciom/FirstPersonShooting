using UnityEngine;

/// <summary>
/// The gun in the player's hands: swaps the visible prop to whatever weapon is
/// selected, and kicks it when it fires.
///
/// WHY IT EXISTS. Every weapon in the arsenal lives on ONE GameObject — all
/// sixty components on the blaster viewmodel, because attaching them per switch
/// is not practical (see WeaponLoadout). So the thing hanging off the camera was
/// the laser blaster forever, whatever was actually firing. With two guns that
/// read as a stylisation; with the whole catalogue unlocked it reads as broken —
/// you pick the Frostbite Beam out of the rack and a laser pistol appears.
///
/// THE PROP IS PRESENTATION ONLY. Shots still originate from the blaster host's
/// own muzzle transform, which is where every weapon's <see cref="Weapon.muzzle"/>
/// already points and where TransformMode re-homes them for vehicle form. A
/// viewmodel that owned the muzzle would have to hand it back correctly on every
/// swap, every fold and every de-rez, and would move the point shots come from
/// each time a model came out of the pipeline half a centimetre longer.
///
/// The host keeps its components and loses only its renderers — it is still
/// where the weapons live, where the muzzle is, and where the muzzle light
/// flashes. This owns that hiding outright, and switches off the host's
/// <see cref="HeldWeaponMount"/> so the two do not fight over the same
/// renderers: on the player that component only ever did the vehicle-form hide,
/// which is handled here.
///
/// Sits under the camera, so it aims with the view.
/// </summary>
public class WeaponViewModel : MonoBehaviour
{
    /// <summary>Where the prop hangs, in camera space: down and to the right.</summary>
    static readonly Vector3 RestPosition = new Vector3(0.26f, -0.24f, 0.42f);

    /// <summary>Tipped in toward the centre of the screen, the way a held gun sits.</summary>
    static readonly Vector3 RestEuler = new Vector3(0f, -6f, 0f);

    /// <summary>How far the prop rides back at full kick, in metres.</summary>
    const float KickDistance = 0.06f;

    /// <summary>Degrees the muzzle lifts at full kick.</summary>
    const float KickPitch = 7f;

    /// <summary>How fast the kick decays, in units of kick per second.</summary>
    const float KickRecovery = 6.5f;

    /// <summary>
    /// Muzzle-light intensity that counts as a full kick. Weapon.FlashMuzzle
    /// defaults to 3; anything brighter is a weapon that asked for a bigger
    /// flash, and it gets the same full shove rather than a bigger one.
    /// </summary>
    const float FullFlash = 3f;

    PlayerBrain _brain;
    TransformMode _vehicle;
    Transform _rig;
    GameObject _prop;
    Weapon _shown;
    float _kick;

    // The blaster the whole arsenal is bolted to: hidden while this is running,
    // and put back exactly as found if this ever goes away.
    GameObject _host;
    Renderer[] _hostRenderers;
    HeldWeaponMount _hostMount;
    bool _hostMountWasEnabled;

    Transform _flashMuzzle;
    Light _flash;

    /// <summary>
    /// Attach the viewmodel to a player. Done in code rather than in the scene
    /// so no rebuild is needed, and idempotent so it can be called on every
    /// match start.
    /// </summary>
    public static WeaponViewModel Ensure(PlayerBrain brain, Transform head)
    {
        if (brain == null || head == null)
            return null;

        var existing = head.GetComponentInChildren<WeaponViewModel>(true);
        if (existing != null)
            return existing;

        var go = new GameObject("WeaponViewModel");
        go.transform.SetParent(head, false);
        var view = go.AddComponent<WeaponViewModel>();
        view._brain = brain;
        return view;
    }

    void Awake()
    {
        if (_brain == null)
            _brain = GetComponentInParent<PlayerBrain>();
        _vehicle = GetComponentInParent<TransformMode>();

        // A child of our own rather than props parented straight here, so the
        // kick moves the whole rig without every model having to agree on where
        // its origin is.
        var rigGo = new GameObject("Rig");
        rigGo.transform.SetParent(transform, false);
        _rig = rigGo.transform;
        _rig.localPosition = RestPosition;
        _rig.localRotation = Quaternion.Euler(RestEuler);
    }

    void OnDisable()
    {
        RestoreHost();
        Clear();
    }

    void LateUpdate()
    {
        // Driving hides it outright, exactly as the shoulder prop does: the tank
        // shoots out of its own turret, and a rifle floating over the hull is
        // just a prop with nowhere to be. Mid-fold counts as driving.
        bool driving = _vehicle != null && (_vehicle.IsVehicle || _vehicle.IsBusy);
        Weapon wanted = driving || _brain == null ? null : _brain.ActiveWeapon();

        // A weapon with no model of its own keeps the blaster that is already
        // there, rather than being handed WeaponArt's fallback silhouette.
        //
        // The fallback is the right floor for the RACK, where the alternative is
        // an empty card — but here the alternative is the imported blaster,
        // which looks better than a box with a glowing end. So the swap only
        // happens for weapons that have really been through the pipeline, and
        // the arsenal improves one gun at a time instead of all sixty getting
        // worse at once.
        bool ownProp = wanted != null && WeaponArt.HasModel(wanted);
        if (ownProp)
            TakeOverHost();
        else
            RestoreHost();

        Weapon show = ownProp ? wanted : null;
        if (show != _shown)
        {
            Clear();
            _shown = show;
            if (show != null)
                _prop = WeaponArt.BuildProp(show, _rig);
        }

        UpdateKick(wanted);
        _rig.localPosition = RestPosition - Vector3.forward * (KickDistance * _kick);
        _rig.localRotation = Quaternion.Euler(RestEuler + new Vector3(-KickPitch * _kick, 0f, 0f));
    }

    /// <summary>
    /// Shove the gun back on every shot, read off the shared muzzle light.
    ///
    /// The light rather than a per-weapon callback because it is the one signal
    /// every weapon already raises when it fires, whatever else it does — a beam
    /// that never launches a projectile still flashes, and so still shakes. It
    /// also costs the weapons nothing: none of them know this exists.
    /// </summary>
    void UpdateKick(Weapon wanted)
    {
        var muzzle = wanted != null ? wanted.muzzle : null;
        if (muzzle != _flashMuzzle)
        {
            _flashMuzzle = muzzle;
            var found = muzzle != null ? muzzle.Find("MuzzleLight") : null;
            _flash = found != null ? found.GetComponent<Light>() : null;
        }

        float fired = _flash != null ? Mathf.Clamp01(_flash.intensity / FullFlash) : 0f;
        // Max, not assignment: the light decays on its own schedule and we want
        // the sharper of the two, so a fast repeater stays shoved back rather
        // than stuttering between the flash and our own recovery.
        _kick = Mathf.Max(fired, Mathf.MoveTowards(_kick, 0f, KickRecovery * Time.deltaTime));
    }

    /// <summary>
    /// Hide the blaster the arsenal is bolted to, once its renderers exist.
    ///
    /// Re-checked rather than done in Awake: the player's model — and the
    /// blaster with it — is built at match start, and a robot swap between
    /// matches rebuilds it again.
    /// </summary>
    void TakeOverHost()
    {
        var host = _brain != null && _brain.ActiveWeapon() != null
            ? _brain.ActiveWeapon().gameObject : null;
        // Tracked by the object, not by the mount: a host with no HeldWeaponMount
        // would otherwise look un-taken every frame and re-scan its renderers
        // for the life of the match.
        if (host == null || host == _host)
            return;

        RestoreHost();
        _host = host;

        _hostMount = host.GetComponent<HeldWeaponMount>();
        if (_hostMount != null)
        {
            _hostMountWasEnabled = _hostMount.enabled;
            _hostMount.enabled = false;
        }

        _hostRenderers = host.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in _hostRenderers)
            if (renderer != null)
                renderer.enabled = false;
    }

    /// <summary>Give the blaster back. A no-op when it was never taken.</summary>
    void RestoreHost()
    {
        if (_hostRenderers != null)
            foreach (var renderer in _hostRenderers)
                if (renderer != null)
                    renderer.enabled = true;
        _hostRenderers = null;

        if (_hostMount != null)
            _hostMount.enabled = _hostMountWasEnabled;
        _hostMount = null;
        _host = null;
    }

    void Clear()
    {
        if (_prop != null)
            Destroy(_prop);
        _prop = null;
        _shown = null;
    }
}
