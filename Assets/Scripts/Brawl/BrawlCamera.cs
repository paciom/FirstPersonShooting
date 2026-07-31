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

    // How far the camera midpoint may chase the fighters — slightly inside
    // the lane so the frame never slides off the stage ends.
    const float TrackHalf = 6.5f;

    const float NearDistance = 8f;
    const float FarDistance = 14f;

    Transform _a, _b;
    float _x;
    float _distance = NearDistance;
    float _shakeAmplitude;
    float _obstruction;

    public void SetTargets(Transform a, Transform b)
    {
        _a = a;
        _b = b;
        Solve(out _x, out _distance);
        Place(Vector3.zero);
    }

    /// <summary>A landed hit's thump. Amplitude in metres, decays on its own.</summary>
    public void Kick(float amplitude)
    {
        _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
    }

    void Solve(out float x, out float distance)
    {
        x = Mathf.Clamp((_a.position.x + _b.position.x) * 0.5f, -TrackHalf, TrackHalf);
        float separation = Mathf.Abs(_a.position.x - _b.position.x);
        distance = Mathf.Lerp(NearDistance, FarDistance,
            Mathf.InverseLerp(2f, 10f, separation));
    }

    void LateUpdate()
    {
        if (_a == null || _b == null)
            return;

        Solve(out float wantedX, out float wantedDistance);
        float ease = 1f - Mathf.Exp(-6f * Time.deltaTime);
        _x = Mathf.Lerp(_x, wantedX, ease);
        _distance = Mathf.Lerp(_distance, wantedDistance, ease);

        _shakeAmplitude = Mathf.Lerp(_shakeAmplitude, 0f, 1f - Mathf.Exp(-8f * Time.deltaTime));
        Vector3 shake = _shakeAmplitude * new Vector3(
            Mathf.PerlinNoise(Time.time * 23f, 0.31f) - 0.5f,
            Mathf.PerlinNoise(0.73f, Time.time * 27f) - 0.5f,
            0f);

        Place(shake);
    }

    void Place(Vector3 shake)
    {
        // The gaze point rides at chest height between the fighters; shake
        // moves it at half strength so a thump reads as a jolt, not a pan.
        Vector3 gaze = new Vector3(_x, 1.1f, 0f) + shake * 0.5f;
        Vector3 desired = new Vector3(_x, 2.3f, -_distance);

        // Auto-avoid: nothing gets to stand between the lens and the fight.
        // A sphere-cast from the fight toward the desired spot finds the
        // first blocker (remix arenas are full of them); the camera pulls
        // in front of it and rises a little to peek over. Corrections come
        // on fast and relax slowly, so a passing pillar doesn't pump zoom.
        Vector3 line = desired - gaze;
        float length = line.magnitude;
        Vector3 direction = line / Mathf.Max(length, 1e-4f);
        float wantedPullIn = 0f;
        if (Physics.SphereCast(gaze, 0.35f, direction, out var hit, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            wantedPullIn = length - Mathf.Max(3.5f, hit.distance - 0.45f);
        float ease = wantedPullIn > _obstruction ? 14f : 2.5f;
        _obstruction = Mathf.Lerp(_obstruction, wantedPullIn,
            1f - Mathf.Exp(-ease * Time.deltaTime));

        Vector3 position = gaze + direction * (length - _obstruction) + shake;
        position.y += _obstruction * 0.30f;
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(gaze - position, Vector3.up);
    }
}
