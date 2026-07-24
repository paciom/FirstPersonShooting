using UnityEngine;

/// <summary>
/// Auto-directed spectator camera for AI v AI mode: slowly orbits the current
/// subject and cuts to a new bot every few seconds. This is the seed of the
/// full interest-based CameraDirector planned for the broadcast system.
/// </summary>
public class SpectatorCamera : MonoBehaviour
{
    public float switchInterval = 6f;
    public float orbitDegreesPerSecond = 10f;
    public float distance = 7f;
    public float height = 3.2f;
    public float followLerp = 2.5f;

    AIBrain[] _bots;
    Transform _subject;
    EnergyShield _subjectShield;
    float _nextSwitch;
    float _orbitAngle;

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

        _orbitAngle += orbitDegreesPerSecond * Time.deltaTime;

        Vector3 focus = _subject.position + Vector3.up * 1.2f;
        Vector3 desired = focus + Quaternion.Euler(0f, _orbitAngle, 0f) * new Vector3(0f, 0f, -distance)
                        + Vector3.up * (height - 1.2f);
        // Keep the camera inside the arena walls and above the floor.
        desired.x = Mathf.Clamp(desired.x, -18.5f, 18.5f);
        desired.z = Mathf.Clamp(desired.z, -18.5f, 18.5f);
        desired.y = Mathf.Max(desired.y, 1f);

        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(focus - transform.position), 4f * Time.deltaTime);
    }

    void PickSubject()
    {
        _nextSwitch = Time.time + switchInterval;
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
    }
}
