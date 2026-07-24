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
