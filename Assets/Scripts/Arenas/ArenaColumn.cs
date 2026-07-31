using UnityEngine;

/// <summary>
/// Marks a vertical prop — a stalk, pillar, column — that blocks movement
/// but must NEVER count as standable floor. The Brawl terrain probe looks
/// straight through these to the real ground beneath, and the embed
/// self-heal escapes them sideways instead of surfacing on top.
/// </summary>
public class ArenaColumn : MonoBehaviour
{
}
