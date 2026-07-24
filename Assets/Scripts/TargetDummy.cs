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
        // Only the player's own de-rezzes score — allies farming dummies
        // shouldn't inflate the number on the HUD.
        if (_shield.LastAttacker != null && _shield.LastAttacker.GetComponent<PlayerBrain>() != null)
            ScoreKeeper.AddPoint();
    }
}
