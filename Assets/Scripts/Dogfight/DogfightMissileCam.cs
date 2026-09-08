using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The pilot's second eye: a picture-in-picture camera riding the missile
/// they just fired, up in the corner, for exactly as long as the missile is
/// somewhere the main view cannot see it.
///
/// A missile launched ahead usually stays in frame and the inset would only
/// cover the fight, so it stays dark while the main view has the shot. The
/// moment the missile leaves the frame — the target breaks, the pilot turns
/// away — the inset lights, chases the missile from behind with its quarry
/// in view, and holds on the bang for a beat afterwards with the verdict
/// (HIT, FLARED, MISS) so "what happened to my missile" is always answered.
///
/// It reads no input and owns no state the mode cares about: the mode
/// asserts <see cref="Enabled"/> and <see cref="Owner"/> every frame (the
/// WeaponsFree discipline), and everything else is derived from the live
/// missile registry. Runs after the main rig's LateUpdate so the "outside the
/// main camera" test sees this frame's camera, not last frame's.
/// </summary>
[DefaultExecutionOrder(200)]
public class DogfightMissileCam : MonoBehaviour
{
    /// <summary>Chase geometry: close behind and a touch above, looking past
    /// the missile toward its quarry. Tighter than the jet chase — the
    /// subject is a metre long.</summary>
    const float ChaseBack = 4.2f;
    const float ChaseUp = 1.3f;
    const float LookAhead = 12f;
    const float Fov = 70f;

    /// <summary>The inset lights the instant the missile leaves the main
    /// frame, and goes dark only once it has been comfortably INSIDE for a
    /// while — the band plus the delay are the hysteresis that stops a
    /// missile skimming the frame edge from strobing the corner.</summary>
    const float InsideBand = 0.07f;
    const float HideDelay = 0.6f;

    /// <summary>How long the inset holds on the impact after the missile is
    /// gone. The blast and the verdict are the whole point of the shot.</summary>
    const float LingerSeconds = 1.5f;

    /// <summary>The mode's per-frame gate: player card, fight stage.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whose missiles this eye follows — the hero's root, asserted
    /// per frame because the hero pawn respawns.</summary>
    public Transform Owner { get; set; }

    public bool Showing => _camera != null && _camera.enabled;

    Camera _main;
    Camera _camera;
    DogfightHud _hud;

    DogfightMissile _subject;
    Vector3 _position;
    bool _snapped;
    float _insideSince = -1f;
    float _lingerUntil;
    string _verdict;
    Color _verdictColor;

    public static DogfightMissileCam Build(Transform parent, Camera main, DogfightHud hud)
    {
        var go = new GameObject("DogfightMissileCam");
        go.transform.SetParent(parent, false);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = Fov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = DogfightSky.SkyTint;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = main.farClipPlane;
        cam.cullingMask = main.cullingMask;
        // Drawn after the main view, into its own corner of the screen.
        cam.depth = main.depth + 1f;
        cam.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderType = CameraRenderType.Base;
        // The trail glow IS the missile at this size; without bloom the inset
        // shows a grey stick.
        data.renderPostProcessing = true;

        var eye = go.AddComponent<DogfightMissileCam>();
        eye._main = main;
        eye._camera = cam;
        eye._hud = hud;
        return eye;
    }

    void LateUpdate()
    {
        if (!Enabled || _main == null || _hud == null)
        {
            Drop();
            Hide();
            return;
        }

        if (_subject == null)
        {
            if (Time.time < _lingerUntil)
            {
                // Holding on the crater: the camera stays put, the label
                // says what happened.
                Present(_verdict, _verdictColor);
                return;
            }
            Acquire();
            if (_subject == null)
            {
                Hide();
                return;
            }
        }

        Vector3 pos = _subject.transform.position;
        Vector3 heading = _subject.Heading;
        Vector3 aim = pos + heading * LookAhead;
        var quarry = _subject.Quarry;
        if (quarry != null)
        {
            var shield = quarry.GetComponent<EnergyShield>();
            Vector3 target = shield != null ? WeaponUtil.Center(shield) : quarry.position;
            // Half way between "along the missile" and "at the target": the
            // quarry stays in frame while the missile still reads as flying
            // forward rather than being stared at.
            aim = Vector3.Lerp(aim, target, 0.5f);
        }

        bool inside = InsideMain(pos);
        if (!inside)
            _insideSince = -1f;
        else if (_insideSince < 0f)
            _insideSince = Time.time;

        bool want = !inside || (Showing && Time.time - _insideSince < HideDelay);
        if (!want)
        {
            Hide();
            return;
        }

        float dt = Time.deltaTime;
        Vector3 up = DogfightSky.FreeOrientation ? _main.transform.up : Vector3.up;
        Vector3 wanted = pos - heading * ChaseBack + up * ChaseUp;
        if (!_snapped)
        {
            _snapped = true;
            _position = wanted;
        }
        else
        {
            _position = Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-14f * dt));
        }
        transform.position = _position;
        Vector3 look = aim - _position;
        if (look.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(look.normalized, up);

        Present("MISSILE CAM", new Color(1f, 0.75f, 0.2f));
    }

    /// <summary>Is the missile somewhere the pilot can already see it? A
    /// margin inside the frame counts as "seen" only for the purposes of
    /// hiding — showing needs a true exit.</summary>
    bool InsideMain(Vector3 world)
    {
        Vector3 view = _main.WorldToViewportPoint(world);
        if (view.z <= 0f)
            return false;
        float band = Showing ? InsideBand : 0f;
        return view.x >= band && view.x <= 1f - band
            && view.y >= band && view.y <= 1f - band;
    }

    /// <summary>The newest live missile with the owner's name on it. The
    /// registry is in launch order, so the last match is the freshest —
    /// and a subject already being ridden is kept until it is gone, so the
    /// pilot always sees how the FIRST one ended.</summary>
    void Acquire()
    {
        if (Owner == null)
            return;
        var all = DogfightMissile.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var missile = all[i];
            if (missile == null || missile.Bolt == null || missile.Owner != Owner)
                continue;
            if (missile.Result != DogfightMissile.Outcome.Flying)
                continue;
            _subject = missile;
            _subject.Ended += Ended;
            _snapped = false;
            _insideSince = -1f;
            return;
        }
    }

    void Ended(DogfightMissile missile)
    {
        if (missile != _subject)
            return;
        missile.Ended -= Ended;
        _subject = null;
        if (!Showing)
            return;
        switch (missile.Result)
        {
            case DogfightMissile.Outcome.Hit:
                _verdict = "HIT";
                _verdictColor = new Color(0.45f, 1f, 0.5f);
                break;
            case DogfightMissile.Outcome.Flared:
                _verdict = "FLARED";
                _verdictColor = new Color(1f, 0.78f, 0.35f);
                break;
            default:
                _verdict = "MISS";
                _verdictColor = new Color(1f, 0.35f, 0.3f);
                break;
        }
        _lingerUntil = Time.time + LingerSeconds;
    }

    void Drop()
    {
        if (_subject != null)
            _subject.Ended -= Ended;
        _subject = null;
        _lingerUntil = 0f;
    }

    void Present(string label, Color color)
    {
        Rect viewport = _hud.MissileCamViewport();
        _camera.rect = viewport;
        _camera.enabled = true;
        _hud.SetMissileCamFrame(viewport, true, label, color);
    }

    void Hide()
    {
        // Found again rather than trusted: a mid-Play recompile wipes the
        // field, and an inset left rendering with nothing behind it would
        // be the visible symptom.
        if (_camera == null)
            _camera = GetComponent<Camera>();
        if (_camera != null)
            _camera.enabled = false;
        _snapped = false;
        _insideSince = -1f;
        if (_hud != null)
            _hud.SetMissileCamFrame(default, false, null, Color.white);
    }

    void OnDestroy()
    {
        Drop();
    }
}
