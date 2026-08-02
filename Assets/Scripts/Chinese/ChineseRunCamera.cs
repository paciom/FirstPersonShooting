using UnityEngine;

/// <summary>
/// The chase camera for Chinese Run: behind and above the runner, looking down
/// the road at the gate it is closing on.
///
/// It leads rather than follows. The aim point sits well ahead of the robot,
/// because the thing the player is reading — four cards over four robots — is
/// ahead, and a camera centred on the runner's back puts the question in the
/// top strip of the screen where the cards stack on top of each other.
///
/// Two departures from a plain follow-cam, both to keep the road legible:
/// z is followed hard (any lag there reads as the runner sliding backward,
/// which is exactly the failure state and must never be faked), while x and
/// height are followed softly, so a lane change swings the view instead of
/// snapping it.
/// </summary>
public class ChineseRunCamera : MonoBehaviour
{
    const float Height = 5.6f;
    const float Behind = 9.5f;

    /// <summary>How far up the road the camera aims — where the gate will be.</summary>
    const float LookAhead = 14f;
    const float LookHeight = 1.6f;

    /// <summary>Fraction of the runner's own x the camera adopts. Under 1, so
    /// an outside lane still reads as an outside lane.</summary>
    const float LaneFollow = 0.55f;

    Transform _runner;
    float _x;
    float _lookX;
    float _shake;
    bool _snapped;

    public void Follow(Transform runner)
    {
        _runner = runner;
        _snapped = false;
    }

    /// <summary>A knock the player felt — the blast that throws them back.</summary>
    public void Shake(float amount)
    {
        _shake = Mathf.Max(_shake, amount);
    }

    void LateUpdate()
    {
        if (_runner == null)
            return;

        float dt = Time.deltaTime;
        Vector3 target = _runner.position;

        if (!_snapped)
        {
            _snapped = true;
            _x = target.x * LaneFollow;
            _lookX = target.x;
        }
        else
        {
            _x = Mathf.Lerp(_x, target.x * LaneFollow, 1f - Mathf.Exp(-6f * dt));
            _lookX = Mathf.Lerp(_lookX, target.x, 1f - Mathf.Exp(-8f * dt));
        }

        if (_shake > 0.001f)
            _shake = Mathf.Max(0f, _shake - dt * 1.6f);
        Vector3 jolt = _shake > 0.001f
            ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (_shake * 0.4f)
            : Vector3.zero;

        // z is taken raw, never smoothed: the runner's forward speed IS the
        // mode, and a camera easing toward it would show the road slowing down
        // every time the robot sped up.
        transform.position = new Vector3(_x, Height, target.z - Behind) + jolt;
        var look = new Vector3(_lookX, LookHeight, target.z + LookAhead);
        transform.rotation = Quaternion.LookRotation((look - transform.position).normalized, Vector3.up);
    }
}
