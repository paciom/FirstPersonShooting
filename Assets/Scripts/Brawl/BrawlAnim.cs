using UnityEngine;

/// <summary>
/// The animator contract between BrawlMoveForge (which builds the
/// controllers) and BrawlFighter (which fires them). One place for the
/// names so the two can never drift.
/// </summary>
public static class BrawlAnim
{
    public const string Speed = "Speed";       // float — locomotion blend
    public const string Punch = "Punch";       // triggers
    public const string Kick = "Kick";
    public const string FlyKick = "FlyKick";
    public const string Hit = "Hit";
    public const string Knockdown = "Knockdown";
    public const string GetUp = "GetUp";
    public const string KO = "KO";
    public const string Victory = "Victory";
    public const string Blast = "Blast";
    public const string Block = "Block";       // bool — held

    public static readonly int SpeedHash = Animator.StringToHash(Speed);
    public static readonly int BlockHash = Animator.StringToHash(Block);
}
