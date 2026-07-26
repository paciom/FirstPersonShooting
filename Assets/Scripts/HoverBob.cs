using UnityEngine;

/// <summary>
/// Gentle sine bob + sway for hover robots so they feel alive without any
/// skeletal animation. Attach to the character root; it drives the "Body" rig.
/// </summary>
public class HoverBob : MonoBehaviour
{
    public float amplitude = 0.07f;
    public float frequency = 1.6f;
    public float swayDegrees = 2.5f;

    Transform _body;
    Vector3 _basePosition;
    float _phase;

    void Start()
    {
        var body = transform.Find("Body");
        if (body == null)
            return;
        _body = body;
        _basePosition = body.localPosition;
        _phase = Random.value * 10f;
    }

    /// <summary>
    /// Restores the Body rig to its resting pose and switches the bob off.
    /// Called by RobotLocomotion when a legged robot is fitted — a walker that
    /// also floats up and down reads as broken.
    /// </summary>
    public void StopAndReset()
    {
        if (_body == null)
        {
            // Disabled before Start() ever ran: nothing has been offset yet.
            enabled = false;
            return;
        }
        _body.localPosition = _basePosition;
        _body.localRotation = Quaternion.identity;
        enabled = false;
    }

    void Update()
    {
        if (_body == null)
            return;

        float t = Time.time * frequency + _phase;
        _body.localPosition = _basePosition + Vector3.up * (Mathf.Sin(t * Mathf.PI * 2f * 0.5f) * amplitude);
        _body.localRotation = Quaternion.Euler(
            Mathf.Sin(t * 1.3f) * swayDegrees, 0f, Mathf.Cos(t * 0.9f) * swayDegrees);
    }
}
