using UnityEngine;

/// <summary>
/// The move templates: each is a pose over normalized time u ∈ [0,1],
/// applied through BrawlPoseRig against whichever robot is on the bench.
/// Character space: +Z toward the opponent, +Y up, +X the robot's right.
///
/// Weights come from a shared envelope — negative during windup (the same
/// aim, swung the other way, is the anticipation), rising through the
/// strike, easing home in the recovery. Snappy on purpose: 0.3–0.7 s of
/// robot karate suits the toy fiction better than mocap realism would.
/// </summary>
public static class BrawlPoses
{
    /// <summary>
    /// -0.35 → 1 → 0: windup ends at <paramref name="windup"/>, the strike
    /// peaks at <paramref name="strike"/>, the hold releases at
    /// <paramref name="hold"/>.
    /// </summary>
    static float Envelope(float u, float windup, float strike, float hold)
    {
        if (u < windup)
            return -0.35f * Mathf.SmoothStep(0f, 1f, u / windup);
        if (u < strike)
            return Mathf.SmoothStep(-0.35f, 1f, (u - windup) / (strike - windup));
        if (u < hold)
            return 1f;
        return Mathf.SmoothStep(1f, 0f, (u - hold) / (1f - hold));
    }

    public static void Punch(BrawlPoseRig rig, float u)
    {
        float w = Envelope(u, 0.25f, 0.45f, 0.62f);
        // Torso leads, right shoulder toward the opponent.
        rig.Rotate(rig.Chest, Vector3.up, -28f, w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.10f, 0.05f, 1f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.05f, 0.02f, 1f), w);
        // Off hand stays home as a guard.
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.25f, 0.55f, 0.6f), Mathf.Abs(w) * 0.5f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.06f), Mathf.Max(0f, w));
    }

    public static void Kick(BrawlPoseRig rig, float u)
    {
        float w = Envelope(u, 0.30f, 0.52f, 0.66f);
        float lift = Mathf.Max(0f, w);
        // Lean away as the leg rises — the counterweight is the read.
        rig.Rotate(rig.Hips, Vector3.right, -12f, lift);
        rig.Rotate(rig.Chest, Vector3.up, 16f, w);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.45f, 0.9f), w);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.12f, 1f), w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, 0f), lift);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.6f, -0.1f, -0.35f), lift * 0.6f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.5f, 0.15f, 0.3f), lift * 0.6f);
    }

    public static void FlyKick(BrawlPoseRig rig, float u)
    {
        // One-way: the pose holds while airborne, the exit blend unwinds it.
        float w = Mathf.SmoothStep(0f, 1f, u / 0.3f);
        rig.Rotate(rig.Hips, Vector3.right, -22f, w);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.25f, 1f), w);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.18f, 1f), w);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(0f, -0.35f, -0.8f), w);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -1f, -0.35f), w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.65f, 0.5f, -0.4f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.65f, 0.5f, -0.4f), w);
    }

    public static void Block(BrawlPoseRig rig, float u)
    {
        float w = Mathf.SmoothStep(0f, 1f, u / 0.4f);
        // A touch of held-breath bob keeps the loop alive.
        float bob = 0.012f * Mathf.Sin(u * 2f * Mathf.PI);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(-0.15f, 0.25f, 0.75f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.30f, 0.85f, 0.35f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(0.15f, 0.25f, 0.75f), w);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.30f, 0.85f, 0.35f), w);
        rig.Rotate(rig.Chest, Vector3.right, 7f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.07f + bob, 0f), w);
    }

    public static void Hit(BrawlPoseRig rig, float u)
    {
        float w = u < 0.2f
            ? Mathf.SmoothStep(0f, 1f, u / 0.2f)
            : Mathf.SmoothStep(1f, 0f, (u - 0.2f) / 0.8f);
        // The head snaps first, the torso follows, the hips give ground.
        rig.Rotate(rig.Chest, Vector3.right, -14f, w);
        rig.Rotate(rig.Chest, Vector3.up, 10f, w);
        rig.Rotate(rig.Head, Vector3.right, -11f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.02f, -0.10f), w);
    }

    public static void Knockdown(BrawlPoseRig rig, float u)
    {
        // One-way fall; the KO state simply never leaves the last frame.
        float w = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u / 0.85f));
        rig.Rotate(rig.Hips, Vector3.right, -75f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.72f, -0.28f), w);
        rig.Rotate(rig.UpLegR, Vector3.right, 28f, w);
        rig.Rotate(rig.UpLegL, Vector3.right, 22f, w);
        rig.Rotate(rig.LegR, Vector3.right, -18f, w);
        rig.Rotate(rig.LegL, Vector3.right, -14f, w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.9f, 0.15f, -0.2f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.9f, 0.15f, -0.2f), w);
        rig.Rotate(rig.Head, Vector3.right, -12f, w);
    }

    public static void Victory(BrawlPoseRig rig, float u)
    {
        float pump = 0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.35f, 1f, 0.05f), 1f);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.18f, 1f, 0f), 1f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.35f, 1f, 0.05f), 1f);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.18f, 1f, 0f), 1f);
        rig.Rotate(rig.Chest, Vector3.up, 8f * Mathf.Sin(u * 2f * Mathf.PI), 1f);
        rig.Shift(rig.Hips, new Vector3(0f, 0.05f * pump, 0f), 1f);
    }

    public static void Blast(BrawlPoseRig rig, float u)
    {
        float w = Envelope(u, 0.35f, 0.55f, 0.75f);
        // Both palms thrust the bolt out — the Charged Spell Cast of the
        // template world.
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.18f, 0.08f, 1f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.10f, 0.05f, 1f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.18f, 0.08f, 1f), w);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.10f, 0.05f, 1f), w);
        rig.Rotate(rig.Chest, Vector3.right, 5f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.06f, 0.03f), Mathf.Abs(w));
    }
}
