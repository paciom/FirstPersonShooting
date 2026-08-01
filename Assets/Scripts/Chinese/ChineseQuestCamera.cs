using UnityEngine;

/// <summary>
/// The Chinese Quest camera: one wide shot of all five robots that leans in
/// when something happens and settles back when it stops.
///
/// The wide shot is COMPUTED, not authored. Five robots pinned to five fixed
/// posts frame beautifully at 16:9 and lose their corners on an ultrawide
/// monitor or a phone held upright — and losing a corner here means losing an
/// answer. So the rig walks itself backward along a fixed camera angle until
/// every robot's feet, head and shoulders project inside the viewport, at
/// whatever aspect ratio the player actually has. It re-fits on resize for
/// the same reason.
///
/// Nothing here is player-controlled: this mode wants the reader's eyes on
/// the character and the four cards, not on a camera stick.
/// </summary>
public class ChineseQuestCamera : MonoBehaviour
{
    /// <summary>
    /// Downward tilt of the wide shot. The tilt is what separates the far pair
    /// of robots from the near pair vertically — flatten it and all four slide
    /// into the same band across the middle of the frame. Steeper than this
    /// starts looking down on the tops of their heads.
    /// </summary>
    const float Pitch = 30f;

    /// <summary>What the wide shot is aimed at: chest height, just ahead of the hero's post.</summary>
    static readonly Vector3 HomeLook = new Vector3(0f, 1.15f, 0.8f);

    /// <summary>Viewport box every robot must sit inside. Generous — the HUD clamps its own cards.</summary>
    const float MarginX = 0.045f;
    const float MarginY = 0.07f;

    /// <summary>Metres the camera creeps down its own sight line at full lean-in.</summary>
    const float PushIn = 5.5f;

    Camera _camera;
    Vector3[] _points = new Vector3[0];
    float _fittedAspect = -1f;

    Vector3 _homePos;
    Vector3 _position;
    Vector3 _look;

    Transform _focusA;
    Transform _focusB;
    float _weight;        // how far toward the focus the shot is, 0..1
    float _targetWeight;

    float _shake;
    float _drift;

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    /// <summary>
    /// Set the wide shot so that all of these world points are on screen, and
    /// snap to it. Called once with the cast's feet, heads and shoulders.
    /// </summary>
    public void FrameCast(Vector3[] points)
    {
        _points = points ?? new Vector3[0];
        Fit();
        _position = _homePos;
        _look = HomeLook;
        Apply();
    }

    /// <summary>Back to the wide shot.</summary>
    public void Relax()
    {
        _focusA = _focusB = null;
        _targetWeight = 0f;
    }

    /// <summary>Lean in on the pair that is about to trade blows.</summary>
    public void WatchFight(Transform attacker, Transform defender)
    {
        _focusA = attacker;
        _focusB = defender;
        _targetWeight = 0.55f;
    }

    /// <summary>Closer still, on one robot: the winner's pose.</summary>
    public void Celebrate(Transform who)
    {
        _focusA = _focusB = who;
        _targetWeight = 0.75f;
    }

    /// <summary>A knock the player felt.</summary>
    public void Shake(float amount)
    {
        _shake = Mathf.Max(_shake, amount);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // A window resize changes which points fit; a mode this static would
        // otherwise sit with an answer off screen until the player restarts.
        if (_camera != null && !Mathf.Approximately(_camera.aspect, _fittedAspect))
            Fit();

        _weight = Mathf.Lerp(_weight, _targetWeight, 1f - Mathf.Exp(-4.5f * dt));

        Vector3 focus = FocusPoint();
        Vector3 sight = (HomeLook - _homePos).normalized;

        // Lean-in is two moves at once: slide down the sight line so the
        // subject grows, and swing the aim toward it so it lands centre frame.
        Vector3 wantedLook = Vector3.Lerp(HomeLook, focus, _weight * 0.8f);
        Vector3 wantedPos = _homePos + sight * (_weight * PushIn);
        Vector3 lateral = focus - HomeLook;
        lateral.y = 0f;
        wantedPos += lateral * (_weight * 0.28f);

        // Idle sway, so the wide shot is never a still photograph. Killed off
        // as the camera leans in, where it would fight the framing.
        _drift += dt * 0.35f;
        float sway = Mathf.Sin(_drift) * 0.35f * (1f - _weight);
        wantedPos += new Vector3(sway, Mathf.Cos(_drift * 0.7f) * 0.14f * (1f - _weight), 0f);

        _position = Vector3.Lerp(_position, wantedPos, 1f - Mathf.Exp(-5f * dt));
        _look = Vector3.Lerp(_look, wantedLook, 1f - Mathf.Exp(-5f * dt));

        if (_shake > 0.001f)
            _shake = Mathf.Max(0f, _shake - dt * 1.9f);

        Apply();
    }

    Vector3 FocusPoint()
    {
        if (_focusA == null && _focusB == null)
            return HomeLook;
        if (_focusA == null)
            return _focusB.position + Vector3.up * 1.2f;
        if (_focusB == null)
            return _focusA.position + Vector3.up * 1.2f;
        return Vector3.Lerp(_focusA.position, _focusB.position, 0.5f) + Vector3.up * 1.2f;
    }

    void Apply()
    {
        Vector3 jolt = Vector3.zero;
        if (_shake > 0.001f)
            jolt = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (_shake * 0.35f);

        transform.position = _position + jolt;
        transform.rotation = Quaternion.LookRotation((_look - transform.position).normalized, Vector3.up);
    }

    /// <summary>
    /// Walk the wide shot backward until the whole cast is inside the frame.
    ///
    /// Angle and aim are fixed; only the distance moves, so the shot's
    /// character survives the fit — an ultrawide simply gets it from further
    /// away. The loop is bounded and keeps its best try, so a pathological
    /// aspect ratio yields a slightly tight shot rather than an infinite one.
    /// </summary>
    void Fit()
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();
        _fittedAspect = _camera != null ? _camera.aspect : 16f / 9f;

        // Up and back along the fixed tilt: rotating "behind" by the pitch
        // gives the offset from the aim point to the camera.
        Vector3 back = Quaternion.Euler(Pitch, 0f, 0f) * Vector3.back;

        float distance = 12f;
        for (int step = 0; step < 60; step++)
        {
            _homePos = HomeLook + back * distance;
            if (_points.Length == 0 || AllOnScreen())
                return;
            distance += 0.5f;
        }
    }

    bool AllOnScreen()
    {
        // The probe has to answer for the pose being fitted, not the pose the
        // camera is currently smoothing toward, so it is posed first.
        var savedPosition = transform.position;
        var savedRotation = transform.rotation;
        transform.position = _homePos;
        transform.rotation = Quaternion.LookRotation((HomeLook - _homePos).normalized, Vector3.up);

        bool inside = true;
        foreach (var point in _points)
        {
            Vector3 view = _camera.WorldToViewportPoint(point);
            if (view.z <= 0f
                || view.x < MarginX || view.x > 1f - MarginX
                || view.y < MarginY || view.y > 1f - MarginY)
            {
                inside = false;
                break;
            }
        }

        transform.position = savedPosition;
        transform.rotation = savedRotation;
        return inside;
    }
}
