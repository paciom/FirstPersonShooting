using UnityEngine;

/// <summary>
/// The side-on duel camera: X follows the midpoint of the two fighters, pull-
/// back breathes with their separation, and landed hits can kick a short
/// decaying shake. Lives on its own rig, built and destroyed by
/// BrawlController.
/// </summary>
public class BrawlCamera : MonoBehaviour
{
    public const float Fov = 50f;

    const float NearDistance = 8f;
    const float FarDistance = 14f;

    // Viewing azimuths the director may cut between, degrees around the
    // fight (0 = the classic side). Scored by line-of-sight every beat.
    static readonly float[] Angles = { 0f, 28f, -28f, 56f, -56f, 180f };

    Transform _a, _b;
    float _x;
    float _y;
    float _distance = NearDistance;
    float _shakeAmplitude;
    float _obstruction;
    float _azimuth;
    float _azimuthTarget;
    float _rethink;

    // Slightly inside the lane so the frame never slides off the ends.
    static float TrackHalf => BrawlStage.CurrentLaneHalf - 1.5f;

    public void SetTargets(Transform a, Transform b)
    {
        _a = a;
        _b = b;
        Solve(out _x, out _y, out _distance);
        Place(Vector3.zero);
    }

    /// <summary>A landed hit's thump. Amplitude in metres, decays on its own.</summary>
    public void Kick(float amplitude)
    {
        _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
    }

    void Solve(out float x, out float y, out float distance)
    {
        x = Mathf.Clamp((_a.position.x + _b.position.x) * 0.5f, -TrackHalf, TrackHalf);
        // The gaze RIDES the fighters' elevation — a duel on top of remix
        // structures is framed exactly like one on the deck, instead of the
        // camera staring at the ground floor while the fight happens above.
        y = (_a.position.y + _b.position.y) * 0.5f;
        // Vertical splits (one robot up a level) need pull-back too, and
        // more per metre than lateral ones — the frame is wide, not tall.
        float separation = Mathf.Max(
            Mathf.Abs(_a.position.x - _b.position.x),
            Mathf.Abs(_a.position.y - _b.position.y) * 2.2f);
        distance = Mathf.Lerp(NearDistance, FarDistance,
            Mathf.InverseLerp(2f, 10f, separation));
    }

    void LateUpdate()
    {
        if (_a == null || _b == null)
            return;

        Solve(out float wantedX, out float wantedY, out float wantedDistance);
        float ease = 1f - Mathf.Exp(-6f * Time.deltaTime);
        _x = Mathf.Lerp(_x, wantedX, ease);
        _y = Mathf.Lerp(_y, wantedY, ease);
        _distance = Mathf.Lerp(_distance, wantedDistance, ease);

        // The director's beat: every so often, score the candidate angles
        // by line-of-sight and swing to the clearest — remix arenas are
        // full of pillars, and rotating around one beats zooming through.
        _rethink -= Time.deltaTime;
        if (_rethink <= 0f)
        {
            _rethink = 0.7f;
            ChooseAzimuth();
        }
        _azimuth = Mathf.MoveTowardsAngle(_azimuth, _azimuthTarget, 45f * Time.deltaTime);

        _shakeAmplitude = Mathf.Lerp(_shakeAmplitude, 0f, 1f - Mathf.Exp(-8f * Time.deltaTime));
        Vector3 shake = _shakeAmplitude * new Vector3(
            Mathf.PerlinNoise(Time.time * 23f, 0.31f) - 0.5f,
            Mathf.PerlinNoise(0.73f, Time.time * 27f) - 0.5f,
            0f);

        Place(shake);
    }

    static Vector3 AzimuthDirection(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, -Mathf.Cos(radians));
    }

    /// <summary>
    /// How far the view is clear from the gaze along a direction. Sphere
    /// AND plain ray, nearer hit wins: the sphere alone skips any wall it
    /// starts touching — which is precisely a fight pressed against one —
    /// and that blind spot parked the camera OUTSIDE the building while
    /// the robots fought inside. The gaze itself is always in open air, so
    /// the zero-radius ray never starts inside anything.
    /// </summary>
    static float Occlusion(Vector3 gaze, Vector3 direction, float length)
    {
        float clear = length;
        if (Physics.SphereCast(gaze, 0.35f, direction, out var sphereHit, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            clear = sphereHit.distance;
        if (Physics.Raycast(gaze, direction, out var rayHit, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            clear = Mathf.Min(clear, rayHit.distance);
        return clear;
    }

    void ChooseAzimuth()
    {
        Vector3 gaze = new Vector3(_x, _y + 1.1f, BrawlStage.LaneZ);
        float bestScore = float.MinValue;
        float best = _azimuthTarget;
        foreach (var candidate in Angles)
        {
            Vector3 offset = AzimuthDirection(candidate) * _distance + Vector3.up * 1.2f;
            Vector3 end = gaze + offset;
            float clear = Occlusion(gaze, offset.normalized, _distance);
            // An end position embedded in geometry can't be a shot at all.
            if (Physics.CheckSphere(end, 0.32f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                clear -= 100f;
            // Clear sight wins; staying put and the classic side both get
            // a thumb on the scale so the director doesn't fidget.
            float score = clear
                          - Mathf.Abs(Mathf.DeltaAngle(candidate, _azimuthTarget)) * 0.010f
                          - Mathf.Abs(Mathf.DeltaAngle(candidate, 0f)) * 0.012f;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        _azimuthTarget = best;
    }

    void Place(Vector3 shake)
    {
        // The gaze point rides at chest height above the fighters' own
        // level; shake moves it at half strength so a thump reads as a
        // jolt, not a pan. A slow sway keeps even a standoff alive.
        Vector3 gaze = new Vector3(_x, _y + 1.1f, BrawlStage.LaneZ) + shake * 0.5f;
        float swayed = _azimuth + 4f * Mathf.Sin(Time.time * 0.35f);
        Vector3 desired = gaze + AzimuthDirection(swayed) * _distance + Vector3.up * 1.2f;

        // Auto-avoid: nothing gets to stand between the lens and the fight.
        // A sphere-cast from the fight toward the desired spot finds the
        // first blocker (remix arenas are full of them); the camera pulls
        // in front of it and rises a little to peek over. Corrections come
        // on fast and relax slowly, so a passing pillar doesn't pump zoom.
        Vector3 line = desired - gaze;
        float length = line.magnitude;
        Vector3 direction = line / Mathf.Max(length, 1e-4f);
        float clearAlong = Occlusion(gaze, direction, length);
        float wantedPullIn = 0f;
        if (clearAlong < length)
            // Close-up beats blind: 1.8 m floor, not a wall-embedding 3.5.
            wantedPullIn = length - Mathf.Max(1.8f, clearAlong - 0.45f);
        float ease = wantedPullIn > _obstruction ? 14f : 2.5f;
        _obstruction = Mathf.Lerp(_obstruction, wantedPullIn,
            1f - Mathf.Exp(-ease * Time.deltaTime));

        Vector3 position = gaze + direction * (length - _obstruction) + shake;
        position.y += _obstruction * 0.30f;

        // The final guarantee, catching every cast blind spot at once: if
        // the lens still ends inside geometry, walk it toward the fight
        // until it provably isn't. Extreme close-up beats a wall interior.
        for (int guard = 0;
             guard < 8 && Physics.CheckSphere(position, 0.32f,
                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
             guard++)
            position = Vector3.Lerp(position, gaze + Vector3.up * 0.4f, 0.3f);

        transform.position = position;
        transform.rotation = Quaternion.LookRotation(gaze - position, Vector3.up);
    }
}
