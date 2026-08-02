using UnityEngine;

/// <summary>
/// Base class every weapon derives from. Shared plumbing (muzzle, owner, team,
/// muzzle-flash light) lives here; subclasses implement the firing behaviour in
/// TryFire. Both PlayerBrain and AIBrain drive weapons through this one
/// interface, so any weapon works for the player, AI bots, and AI-v-AI mode.
///
/// Contract: TryFire is called every frame the trigger is held; `direction`
/// is a normalized world-space aim vector. Semi-auto weapons gate themselves on
/// a cooldown; continuous weapons (beam) and charge weapons (rail) use the
/// per-frame calls directly.
/// </summary>
public abstract class Weapon : MonoBehaviour
{
    [Header("Weapon")]
    public string weaponName = "Weapon";
    public Color color = new Color(0.2f, 0.9f, 1f);
    public float damage = 20f;
    public float range = 60f;

    [Tooltip("Distance the AI likes to fight at with this weapon (drives bot approach + weapon choice).")]
    public float preferredRange = 20f;

    [Tooltip("Shots/effects originate here; defaults to this transform.")]
    public Transform muzzle;

    [Tooltip("Root of the firing character — its own colliders are ignored.")]
    public Transform ownerRoot;

    protected int TeamId { get; private set; }

    Light _muzzleLight;

    protected virtual void Awake()
    {
        if (muzzle == null) muzzle = transform;
        if (ownerRoot == null) ownerRoot = transform.root;

        var shield = ownerRoot.GetComponent<EnergyShield>();
        TeamId = shield != null ? shield.teamId : -1;

        EnsureMuzzleLight();
    }

    /// <summary>
    /// The flash light, on whichever muzzle this weapon is firing out of now.
    ///
    /// Re-homed rather than found once, because <see cref="muzzle"/> moves: a
    /// tank's siege kit fires out of the turret barrel and hands the gun back to
    /// the blaster when the robot stands up (see TransformMode), and a light
    /// left behind on the old muzzle flashes somewhere the shots no longer come
    /// from. The one it leaves behind is harmless — it decays to zero and stays
    /// there until the weapon comes back to it.
    ///
    /// All weapons on one muzzle share a single light — with a full arsenal of
    /// 50+ weapons per character, one light each would swamp the renderer.
    /// </summary>
    void EnsureMuzzleLight()
    {
        if (muzzle == null)
            return;
        if (_muzzleLight != null && _muzzleLight.transform.parent == muzzle)
            return;

        var existing = muzzle.Find("MuzzleLight");
        if (existing != null)
        {
            _muzzleLight = existing.GetComponent<Light>();
            return;
        }

        var lightGo = new GameObject("MuzzleLight");
        lightGo.transform.SetParent(muzzle, false);
        _muzzleLight = lightGo.AddComponent<Light>();
        _muzzleLight.type = LightType.Point;
        _muzzleLight.color = color;
        _muzzleLight.range = 4.5f;
        _muzzleLight.intensity = 0f;
    }

    /// <summary>Called each frame the trigger is held. Direction is normalized.</summary>
    public abstract void TryFire(Vector3 direction);

    /// <summary>Light up the muzzle this frame; it decays automatically.</summary>
    protected void FlashMuzzle(float intensity = 3f)
    {
        EnsureMuzzleLight();
        if (_muzzleLight != null)
        {
            _muzzleLight.color = color;   // shared light takes the firer's hue
            _muzzleLight.intensity = Mathf.Max(_muzzleLight.intensity, intensity);
        }
    }

    protected virtual void LateUpdate()
    {
        if (_muzzleLight != null && _muzzleLight.intensity > 0f)
            _muzzleLight.intensity = Mathf.MoveTowards(_muzzleLight.intensity, 0f, 45f * Time.deltaTime);
    }

    /// <summary>Shared hit resolution: damage an opposing shield or a cover block, spark the surface.</summary>
    protected void ApplyHit(RaycastHit hit, float amount)
    {
        var shield = hit.transform.root.GetComponent<EnergyShield>();
        if (shield != null && shield.teamId != TeamId)
            shield.TakeHit(amount, hit.point, ownerRoot);
        else
            WeaponUtil.DamageProp(hit.collider, amount, hit.point);
        VfxUtil.ImpactBurst(hit.point + hit.normal * 0.05f, color);
    }
}
