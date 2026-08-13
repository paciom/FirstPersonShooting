using UnityEngine;

/// <summary>
/// The price tag a defender robot wears for the rest of its life. Stamped by
/// TDGarrison at hire so the difficulty director can appraise the standing
/// corps by what it actually cost — a titan and a scout are not the same
/// amount of defense, and nothing else about a spawned CommanderUnit
/// remembers which it was.
/// </summary>
public class TDHireWorth : MonoBehaviour
{
    [SerializeField] public int worth;
}
