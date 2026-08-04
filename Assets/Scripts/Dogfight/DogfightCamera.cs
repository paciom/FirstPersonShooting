using UnityEngine;

/// <summary>
/// The dogfight's eye: a chase view and a cockpit view of one subject jet,
/// with the AI WAR broadcast cutting between subjects on a clock.
///
/// CHASE is the default and the tutorial: jet low in frame, horizon honest,
/// bank leaning the camera just enough to feel the turn without costing anyone
/// their stomach. COCKPIT is the toggle both cards advertise on C — in it the
/// subject's own airframe is hidden, because a canopy over the lens is a
/// windscreen made of your own head (FirstPersonBody's reasoning, one class
/// further up the sky).
///
/// The rig reads no input. The mode is the only thing in DOGFIGHT that touches
/// the keyboard, and it calls <see cref="ToggleView"/> / <see cref="Next"/> —
/// the TankRaid division of labour.
/// </summary>
public class DogfightCamera : MonoBehaviour
{
    /// <summary>The broadcast clock. Long enough to read a chase, short enough
    /// that the jet you are not watching stays a character.</summary>
    public const float CutSeconds = 7f;

    /// <summary>How long the broadcast sits on a crater after the wreck it
    /// was riding meets the deck. Less than the mode's respawn beat, so the
    /// cut always lands before the respawn yanks the subject away.</summary>
    const float WreckLingerSeconds = 1.8f;

    const float ChaseBack = 9f;
    const float ChaseUp = 3.1f;
    const float LookAhead = 14f;

    /// <summary>How much of the subject's bank the camera adopts in chase.</summary>
    const float LeanFraction = 0.35f;

    const float BaseFov = 62f;
    const float BoostFov = 74f;

    public bool Cockpit { get; private set; }

    /// <summary>Auto-cut between jets — the AI WAR broadcast. Player mode
    /// leaves it off and the subject pinned.</summary>
    public bool Broadcast { get; set; }

    public JetPawn Subject { get; private set; }

    Camera _camera;
    float _nextCut;
    float _shake;
    Vector3 _position;
    bool _snapped;
    bool _hidSubject;

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    public void Follow(JetPawn subject)
    {
        if (Subject != null && Subject != subject)
            Subject.SetVisible(true);
        Subject = subject;
        _hidSubject = false;
        _snapped = false;
        _nextCut = Time.time + CutSeconds;
    }

    public void ToggleView()
    {
        Cockpit = !Cockpit;
        if (!Cockpit && Subject != null)
            Subject.SetVisible(true);
    }

    /// <summary>A kill anywhere is THE shot: the broadcast drops what it was
    /// doing and rides the wreck down. Its own linger clock decides when to
    /// look away again.</summary>
    public void CoverWreck(JetPawn wreck)
    {
        if (wreck != null && wreck != Subject)
            Follow(wreck);
    }

    /// <summary>The broadcast's "next jet" — SPACE, or the cut clock. Prefers
    /// the living: a camera that cuts from one crater to another is a war
    /// correspondent, not a sports broadcast.</summary>
    public void Next()
    {
        var all = JetPawn.All;
        if (all.Count == 0)
            return;
        int from = 0;
        for (int i = 0; i < all.Count; i++)
            if (all[i] == Subject)
                from = i;
        JetPawn fallback = null;
        for (int step = 1; step <= all.Count; step++)
        {
            var candidate = all[(from + step) % all.Count];
            if (candidate == null)
                continue;
            if (!candidate.IsDown)
            {
                Follow(candidate);
                return;
            }
            if (fallback == null)
                fallback = candidate;
        }
        if (fallback != null)
            Follow(fallback);
    }

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    void LateUpdate()
    {
        if (Subject == null)
            return;

        // A falling wreck is never cut away from: the clock is held at the
        // linger length until impact, so the countdown that finally cuts
        // starts at the crater, not at the kill.
        if (Broadcast && Subject.IsDown && !Subject.WreckLanded)
            _nextCut = Time.time + WreckLingerSeconds;

        if (Broadcast && Time.time >= _nextCut)
            Next();

        float dt = Time.deltaTime;
        if (_shake > 0.001f)
            _shake = Mathf.Max(0f, _shake - dt * 1.8f);
        Vector3 jolt = _shake > 0.001f
            ? Random.insideUnitSphere * (_shake * 0.4f)
            : Vector3.zero;

        var jet = Subject.transform;
        float speedNorm = Mathf.InverseLerp(JetPawn.SpeedMin, JetPawn.SpeedBoost,
            Subject.ReadoutSpeed);
        _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView,
            Mathf.Lerp(BaseFov, BoostFov, speedNorm), 1f - Mathf.Exp(-4f * dt));

        if (Cockpit)
        {
            // The one view where the airframe would be in the way.
            Subject.SetVisible(false);
            _hidSubject = true;
            transform.position = Subject.CockpitAnchor + jolt * 0.4f;
            transform.rotation = Quaternion.Slerp(transform.rotation, jet.rotation,
                1f - Mathf.Exp(-20f * dt));
            return;
        }

        // Restored on the TRANSITION out of cockpit, not asserted every frame
        // — the spawn-grace blink also drives visibility, and a camera that
        // re-shows the subject every LateUpdate erases it.
        if (_hidSubject)
        {
            _hidSubject = false;
            Subject.SetVisible(true);
        }
        Vector3 wanted = jet.position - jet.forward * ChaseBack + Vector3.up * ChaseUp;
        if (!_snapped)
        {
            _snapped = true;
            _position = wanted;
        }
        else
        {
            _position = Vector3.Lerp(_position, wanted, 1f - Mathf.Exp(-8f * dt));
        }
        transform.position = _position + jolt;

        // Lean a fraction of the subject's bank: enough to feel the turn,
        // level enough that the horizon stays a horizon.
        Vector3 up = Vector3.Slerp(Vector3.up, jet.up, LeanFraction);
        transform.rotation = Quaternion.LookRotation(
            (jet.position + jet.forward * LookAhead - transform.position).normalized, up);
    }
}
