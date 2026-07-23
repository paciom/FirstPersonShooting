using UnityEngine;

/// <summary>
/// Awards a point when this character is de-rezzed. Attached to target dummies
/// and AI bots; the DeRezEffect on the same object handles visuals + respawn.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class TargetDummy : MonoBehaviour
{
    EnergyShield _shield;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _shield.OnDeRezzed += HandleDeRez;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= HandleDeRez;
    }

    void HandleDeRez()
    {
        ScoreKeeper.AddPoint();
    }
}
