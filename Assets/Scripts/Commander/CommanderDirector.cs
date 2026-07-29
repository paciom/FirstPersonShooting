using UnityEngine;

/// <summary>
/// The spectator mode's cameraperson: drifts the RTS camera toward wherever
/// the war actually is — the centroid of units in combat, zoomed to fit the
/// engagement's spread — and hands the wheel over the moment a human touches
/// the controls, taking it back after a few idle seconds.
///
/// Sits on the camera rig next to CommanderCamera, only in AI-war sessions.
/// </summary>
public class CommanderDirector : MonoBehaviour
{
    const float RetargetSeconds = 0.6f;

    CommanderCamera _camera;
    Vector3 _target;
    float _spread;
    float _nextRetarget;
    bool _hasTarget;

    void Awake()
    {
        _camera = GetComponent<CommanderCamera>();
    }

    void Update()
    {
        if (_camera == null)
            return;

        if (Time.time >= _nextRetarget)
        {
            _nextRetarget = Time.time + RetargetSeconds;
            Retarget();
        }

        if (_hasTarget)
            _camera.DriftTo(_target, _spread, Time.unscaledDeltaTime);
    }

    /// <summary>
    /// Where to look: fighting units first; failing that, everyone; failing
    /// that, the midfield. Spread (the engagement's radius) drives zoom —
    /// a duel gets a close-up, a brawl gets the wide shot.
    /// </summary>
    void Retarget()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        bool anyCombat = false;

        // Two passes folded into one: prefer combatants, fall back to all.
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || !unit.IsAlive)
                continue;
            if (unit.InCombat)
            {
                if (!anyCombat) { sum = Vector3.zero; count = 0; anyCombat = true; }
                sum += unit.transform.position;
                count++;
            }
            else if (!anyCombat)
            {
                sum += unit.transform.position;
                count++;
            }
        }

        if (count == 0)
        {
            _target = Vector3.zero;   // midfield between the bases
            _spread = 40f;
            _hasTarget = true;
            return;
        }

        Vector3 centroid = sum / count;
        float maxSqr = 0f;
        foreach (var unit in CommanderUnit.All)
        {
            if (unit == null || !unit.IsAlive || (anyCombat && !unit.InCombat))
                continue;
            float sqr = (unit.transform.position - centroid).sqrMagnitude;
            if (sqr > maxSqr)
                maxSqr = sqr;
        }

        _target = centroid;
        _spread = Mathf.Sqrt(maxSqr);
        _hasTarget = true;
    }
}
