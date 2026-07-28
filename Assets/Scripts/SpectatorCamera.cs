using UnityEngine;

/// <summary>
/// Auto-directed spectator camera for AI v AI mode: slowly orbits the current
/// subject and cuts to a new bot every few seconds. This is the seed of the
/// full interest-based CameraDirector planned for the broadcast system.
///
/// Keeping the subject actually VISIBLE is most of the work. Multi-level arenas
/// put upper floors, catwalks and ziggurat tiers between the camera and the
/// fight, so the rig does three things before it settles on a shot:
///
///  * <b>Duck under ceilings</b> — probe upward from the subject and cap the
///    camera's height, so a bot fighting under a catwalk is not filmed through
///    the catwalk.
///  * <b>Swing around blockers</b> — try a spread of orbit angles and prefer one
///    with a clear line. Orbiting past a pillar looks like camerawork; jamming
///    against it looks like a bug.
///  * <b>Pull in as a last resort</b> — when no angle is clear, close the
///    distance to just in front of whatever is in the way.
/// </summary>
public class SpectatorCamera : MonoBehaviour
{
    public float switchInterval = 6f;
    public float orbitDegreesPerSecond = 10f;
    public float distance = 7f;
    public float height = 3.2f;
    public float followLerp = 2.5f;

    [Header("Occlusion")]
    [Tooltip("Radius of the probe used to test the shot. Wider than a ray so the " +
             "camera does not thread a gap it cannot actually see through.")]
    public float probeRadius = 0.35f;

    [Tooltip("Gap left between the camera and whatever blocked it.")]
    public float blockerClearance = 0.45f;

    [Tooltip("Never close nearer than this to the subject, however blocked.")]
    public float minDistance = 2.2f;

    [Tooltip("How fast the shot may swing around a blocker, degrees/second.")]
    public float swingSpeed = 220f;

    /// <summary>Orbit offsets tried, in order of preference — nearest shot first.</summary>
    static readonly float[] CandidateOffsets = { 0f, 35f, -35f, 75f, -75f, 120f, -120f, 180f };

    static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

    AIBrain[] _bots;
    Transform _subject;
    EnergyShield _subjectShield;
    float _nextSwitch;
    float _orbitAngle;

    /// <summary>Smoothed detour around blockers, added to the orbit angle.</summary>
    float _angleOffset;

    // Character roots are skipped by the occlusion probe: a robot crossing frame
    // should not yank the camera in. Refreshed on a slow tick because teams gain
    // robots mid-match (gold-funded reinforcements).
    Transform[] _characterRoots;
    float _nextRootRescan;

    void Start()
    {
        _bots = FindObjectsByType<AIBrain>(FindObjectsSortMode.None);
        _orbitAngle = Random.value * 360f;
        PickSubject();
    }

    void LateUpdate()
    {
        // Cut away when the shot clock expires or the subject de-rezzes —
        // orbiting an invisible robot makes for bad television.
        if (Time.time >= _nextSwitch || _subject == null || !_subject.gameObject.activeInHierarchy
            || (_subjectShield != null && _subjectShield.IsDown))
            PickSubject();
        if (_subject == null)
            return;

        RefreshCharacterRoots();

        _orbitAngle += orbitDegreesPerSecond * Time.deltaTime;

        Vector3 focus = _subject.position + Vector3.up * 1.2f;
        float rise = Mathf.Max(0f, CeilingLimitedRise(focus));

        // Find the offset that frames the subject best, then swing toward it
        // rather than snapping — a hard cut to a new angle reads as a glitch.
        float bestOffset = PickBestOffset(focus, rise);
        _angleOffset = Mathf.MoveTowardsAngle(_angleOffset, bestOffset, swingSpeed * Time.deltaTime);

        // Always re-solve at the angle we actually reached, not the one we are
        // heading for: mid-swing the camera would otherwise fly straight through
        // the blocker it is routing around.
        Vector3 desired = ClearedPoint(focus, OrbitPoint(focus, _orbitAngle + _angleOffset, rise));

        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(focus - transform.position), 4f * Time.deltaTime);
    }

    // ---------- shot solving ----------

    /// <summary>
    /// How far above the subject the camera may sit before it would be filming
    /// through a floor. This is what fixes the level-2 problem: under a catwalk
    /// the shot drops beneath it instead of climbing above it.
    /// </summary>
    float CeilingLimitedRise(Vector3 focus)
    {
        float wanted = height - 1.2f;
        if (wanted <= 0f)
            return wanted;

        float ceiling = CastDistance(focus, Vector3.up, wanted + probeRadius + 0.3f);
        if (ceiling >= wanted + probeRadius + 0.3f)
            return wanted;   // open sky

        // Sit clear of the underside rather than embedded in it.
        return Mathf.Max(0f, ceiling - probeRadius - 0.25f);
    }

    Vector3 OrbitPoint(Vector3 focus, float angle, float rise)
    {
        Vector3 point = focus
                      + Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, -distance)
                      + Vector3.up * rise;

        // Stay inside the ACTIVE arena rather than a hardcoded 40x40 box.
        Vector2 bounds = ArenaContext.HalfExtent + Vector2.one * 2.5f;
        point.x = Mathf.Clamp(point.x, -bounds.x, bounds.x);
        point.z = Mathf.Clamp(point.z, -bounds.y, bounds.y);
        // Nested Max, not the three-argument form: that one takes params float[]
        // and would allocate on every candidate, every frame.
        point.y = Mathf.Max(Mathf.Max(point.y, ArenaContext.GroundY + 1f), focus.y - 1.5f);
        return point;
    }

    /// <summary>
    /// Try each candidate offset and return the first with a completely clear
    /// line to the subject. If none is clear, return whichever kept the camera
    /// furthest back — the widest shot available.
    /// </summary>
    float PickBestOffset(Vector3 focus, float rise)
    {
        float bestOffset = 0f;
        float bestRoom = -1f;

        foreach (float offset in CandidateOffsets)
        {
            Vector3 target = OrbitPoint(focus, _orbitAngle + offset, rise);
            float room = ClearRoom(focus, target, out _);

            // Fully clear: take it. The list is ordered by how far the shot has
            // to move, so the first clear angle is also the least disruptive.
            if (room >= Vector3.Distance(focus, target) - 0.01f)
                return offset;

            if (room > bestRoom)
            {
                bestRoom = room;
                bestOffset = offset;
            }
        }
        return bestOffset;
    }

    Vector3 ClearedPoint(Vector3 focus, Vector3 target)
    {
        ClearRoom(focus, target, out Vector3 cleared);
        return cleared;
    }

    /// <summary>
    /// Distance from <paramref name="focus"/> toward <paramref name="target"/>
    /// that is free of level geometry, and the camera point that fits in it.
    /// </summary>
    float ClearRoom(Vector3 focus, Vector3 target, out Vector3 cleared)
    {
        Vector3 delta = target - focus;
        float span = delta.magnitude;
        if (span < 0.01f)
        {
            cleared = target;
            return 0f;
        }

        Vector3 dir = delta / span;
        float room = CastDistance(focus, dir, span);
        cleared = focus + dir * Mathf.Max(room - blockerClearance, minDistance);
        return room;
    }

    /// <summary>
    /// Sphere-probe distance to the nearest piece of level geometry, ignoring
    /// characters and triggers. Returns <paramref name="maxDistance"/> when
    /// nothing is in the way.
    /// </summary>
    float CastDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        int count = Physics.SphereCastNonAlloc(origin, probeRadius, direction, HitBuffer,
                                               maxDistance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = maxDistance;
        for (int i = 0; i < count; i++)
        {
            var hit = HitBuffer[i];
            if (hit.collider == null || IsCharacter(hit.collider.transform))
                continue;
            // A zero-distance hit means the probe started already overlapping —
            // treat it as no room rather than as "nothing found".
            if (hit.distance < nearest)
                nearest = hit.distance;
        }
        return nearest;
    }

    bool IsCharacter(Transform t)
    {
        if (_characterRoots == null)
            return false;
        Transform root = t.root;
        foreach (var character in _characterRoots)
            if (character != null && character == root)
                return true;
        return false;
    }

    void RefreshCharacterRoots()
    {
        if (Time.time < _nextRootRescan && _characterRoots != null)
            return;
        _nextRootRescan = Time.time + 3f;

        var shields = FindObjectsByType<EnergyShield>(FindObjectsSortMode.None);
        _characterRoots = new Transform[shields.Length];
        for (int i = 0; i < shields.Length; i++)
            _characterRoots[i] = shields[i].transform.root;
    }

    // ---------- subject selection ----------

    void PickSubject()
    {
        _nextSwitch = Time.time + switchInterval;
        // Re-scan on every cut: teams gain robots mid-match, and a reinforcement
        // that can never be cut to is a robot the broadcast never shows.
        _bots = FindObjectsByType<AIBrain>(FindObjectsSortMode.None);
        if (_bots == null || _bots.Length == 0)
            return;

        // Prefer a different, alive bot than the current subject.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var bot = _bots[Random.Range(0, _bots.Length)];
            if (IsWatchable(bot) && bot.transform != _subject)
            {
                SetSubject(bot);
                return;
            }
        }
        foreach (var bot in _bots)
            if (IsWatchable(bot))
            {
                SetSubject(bot);
                return;
            }
    }

    static bool IsWatchable(AIBrain bot)
    {
        if (bot == null || !bot.gameObject.activeInHierarchy)
            return false;
        var shield = bot.GetComponent<EnergyShield>();
        return shield == null || !shield.IsDown;
    }

    void SetSubject(AIBrain bot)
    {
        _subject = bot.transform;
        _subjectShield = bot.GetComponent<EnergyShield>();
        // A cut is the one moment a big angle change is free, so start the new
        // shot from a clean offset instead of inheriting the last detour.
        _angleOffset = 0f;
    }
}
