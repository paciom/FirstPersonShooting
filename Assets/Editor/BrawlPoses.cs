using UnityEngine;

/// <summary>
/// The move templates: full-body kung fu choreography over normalized time
/// u ∈ [0,1], applied through BrawlPoseRig against whichever robot is on the
/// bench. Character space: +Z toward the opponent, +Y up, +X the robot's
/// right.
///
/// The rule every strike follows is the KINETIC CHAIN: hips fire first,
/// torso multiplies the twist, the shoulder rides it, the limb arrives
/// last — while the off arm counter-pulls (the karate hikite), the weight
/// sinks then drives forward, and the head counter-rotates to keep the eyes
/// on the opponent. A punch that only lifts an arm reads as a mannequin;
/// these read as intent.
///
/// Every move also sits on <see cref="StanceBase"/> — the bladed guard that
/// is the Brawl idle — so clips begin and end in the stance the idle loop
/// holds, and transitions land instead of snapping.
///
/// Timing runs chamber → strike → recover through <see cref="Pulse"/>
/// windows rather than one peak: the coil is what sells the explosion.
/// </summary>
public static class BrawlPoses
{
    /// <summary>0 → 1 across [rise0,rise1], hold 1, 1 → 0 across [fall0,fall1].</summary>
    static float Pulse(float u, float rise0, float rise1, float fall0, float fall1)
    {
        if (u < rise0) return 0f;
        if (u < rise1) return Mathf.SmoothStep(0f, 1f, (u - rise0) / (rise1 - rise0));
        if (u < fall0) return 1f;
        if (u < fall1) return Mathf.SmoothStep(1f, 0f, (u - fall0) / (fall1 - fall0));
        return 0f;
    }

    /// <summary>
    /// The fighting stance everything grows from: hips quarter-bladed with
    /// the chest a touch more, eyes still front, sitting into staggered
    /// legs (right leads), fists up at chin height. Weight 1 = full guard.
    /// </summary>
    public static void StanceBase(BrawlPoseRig rig, float w)
    {
        if (w <= 0f)
            return;
        rig.Rotate(rig.Hips, Vector3.up, 14f, w);
        rig.Rotate(rig.Chest, Vector3.up, 10f, w);
        rig.Rotate(rig.Head, Vector3.up, -18f, w);   // eyes stay on the foe

        rig.Shift(rig.Hips, new Vector3(0f, -0.07f, 0f), w);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.06f, -0.85f, 0.42f), w);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -1f, -0.12f), w);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.06f, -0.85f, -0.30f), w);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -1f, 0.18f), w);

        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.30f, -0.55f, 0.55f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.05f, 0.60f, 0.75f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.30f, -0.55f, 0.55f), w);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.05f, 0.60f, 0.75f), w);
    }

    /// <summary>The idle loop: the guard, breathing. First and last frames match.</summary>
    public static void Stance(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        float breathe = Mathf.Sin(u * 2f * Mathf.PI);
        rig.Shift(rig.Hips, new Vector3(0f, 0.014f * breathe, 0.010f * Mathf.Sin(u * 4f * Mathf.PI)), 1f);
        rig.Rotate(rig.Chest, Vector3.up, 2.5f * breathe, 1f);
        // The guard hands never sit dead still.
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.05f, 0.62f, 0.75f), 0.25f + 0.15f * breathe);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.05f, 0.62f, 0.75f), 0.25f - 0.15f * breathe);
    }

    public static void Punch(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        float chamber = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float strike = Pulse(u, 0.20f, 0.38f, 0.58f, 1.00f);

        // Chamber: the right side loads — hips and chest coil away, the
        // fist draws back to the hip, weight sinks onto the rear leg.
        rig.Rotate(rig.Hips, Vector3.up, 12f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 16f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.45f, -0.75f, -0.45f), chamber);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.25f, -0.20f, -0.90f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, -0.05f), chamber);

        // Strike: the chain fires — hips whip through, chest doubles the
        // twist, the fist arrives; the left hand snaps back to the hip
        // (hikite) and the rear leg drives the lunge.
        rig.Rotate(rig.Hips, Vector3.up, -22f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -34f, strike);
        rig.Rotate(rig.Head, Vector3.up, 30f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.06f, 0.02f, 1f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.02f, 0f, 1f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.40f, -0.70f, -0.50f), strike);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.15f, -0.25f, -0.85f), strike);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.05f, -0.80f, -0.55f), strike * 0.7f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.02f, 0.14f), strike);
    }

    public static void Jab(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Quicker and shallower than the cross: less coil, snap out, snap home.
        float chamber = Pulse(u, 0.00f, 0.12f, 0.16f, 0.36f);
        float strike = Pulse(u, 0.16f, 0.32f, 0.52f, 1.00f);
        rig.Rotate(rig.Chest, Vector3.up, 8f, chamber);
        rig.Rotate(rig.Hips, Vector3.up, -10f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -18f, strike);
        rig.Rotate(rig.Head, Vector3.up, 16f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.05f, 0.06f, 1f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.02f, 0.03f, 1f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.02f, 0.07f), Mathf.Max(0f, strike));
    }

    public static void Hook(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The LEFT hand: winds wide, arcs across the centreline, hips
        // driving the turn the whole way.
        float chamber = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float strike = Pulse(u, 0.20f, 0.38f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, -14f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, -20f, chamber);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.85f, 0.15f, 0.15f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, 22f, strike);
        rig.Rotate(rig.Chest, Vector3.up, 34f, strike);
        rig.Rotate(rig.Head, Vector3.up, -30f, strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.15f, 0.12f, 0.95f), strike);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.35f, 0.05f, 0.85f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.25f, 0.55f, 0.6f), Mathf.Abs(strike) * 0.5f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.06f), Mathf.Max(0f, strike));
    }

    public static void Uppercut(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Sinks low, then the whole frame drives UP through the fist.
        float chamber = Pulse(u, 0.00f, 0.18f, 0.22f, 0.44f);
        float strike = Pulse(u, 0.22f, 0.40f, 0.58f, 1.00f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.14f, 0f), chamber);
        rig.Rotate(rig.Chest, Vector3.right, 14f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.25f, -0.85f, 0.25f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -16f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -22f, strike);
        rig.Rotate(rig.Chest, Vector3.right, -16f, strike);
        rig.Rotate(rig.Head, Vector3.up, 18f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.08f, 0.55f, 0.65f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.02f, 0.85f, 0.45f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, 0.06f, 0.05f), Mathf.Max(0f, strike));
    }

    public static void Elbow(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Short-range: the folded arm drives across, all torso.
        float chamber = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float strike = Pulse(u, 0.20f, 0.38f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 14f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 20f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.75f, 0.15f, -0.2f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -24f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -36f, strike);
        rig.Rotate(rig.Head, Vector3.up, 32f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(-0.05f, 0.2f, 0.95f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.75f, 0.15f, 0.35f), strike);   // stays folded
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.09f), Mathf.Max(0f, strike));
    }

    public static void Backfist(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The spin sells it: the fist folds across the chest while the
        // whole frame winds, then hips-chest-arm release in order and the
        // knuckles whip out on the end of the uncoil.
        float chamber = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float strike = Pulse(u, 0.20f, 0.38f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 26f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 34f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(-0.35f, 0.30f, 0.35f), chamber);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.55f, 0.15f, 0.45f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.06f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -40f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -26f, strike);
        rig.Rotate(rig.Head, Vector3.up, 50f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.45f, 0.20f, 0.85f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.30f, 0.10f, 0.95f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.40f, -0.60f, -0.45f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.10f), strike);
    }

    public static void Hammerfist(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Cocks high behind the head, then the whole trunk crunches forward
        // and drops its weight through the falling fist.
        float chamber = Pulse(u, 0.00f, 0.18f, 0.22f, 0.44f);
        float strike = Pulse(u, 0.22f, 0.40f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 10f, chamber);
        rig.Rotate(rig.Chest, Vector3.right, -14f, chamber);   // arches back
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.30f, 0.95f, 0.05f), chamber);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.15f, 0.85f, -0.25f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.04f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -14f, strike);
        rig.Rotate(rig.Chest, Vector3.right, 20f, strike);      // the crunch
        rig.Rotate(rig.Head, Vector3.right, 8f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.10f, 0.05f, 0.98f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.02f, -0.25f, 0.95f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.35f, 0.30f, 0.45f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.07f, 0.10f), strike);
    }

    public static void PalmStrike(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The LEFT palm heel: draws to the hip, then the hips drive it
        // straight through the centre — a shove with the whole body in it.
        float chamber = Pulse(u, 0.00f, 0.14f, 0.18f, 0.40f);
        float strike = Pulse(u, 0.18f, 0.36f, 0.56f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, -12f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, -16f, chamber);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.40f, -0.55f, -0.45f), chamber);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.20f, -0.15f, -0.85f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, -0.04f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, 20f, strike);
        rig.Rotate(rig.Chest, Vector3.up, 30f, strike);
        rig.Rotate(rig.Head, Vector3.up, -26f, strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.05f, 0.08f, 1f), strike);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.02f, 0.30f, 0.90f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.40f, -0.65f, -0.45f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.02f, 0.12f), strike);
    }

    public static void Chop(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The knife hand rises beside the ear and scythes down across the
        // centreline, torso wheeling with it.
        float chamber = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float strike = Pulse(u, 0.20f, 0.38f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 12f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 16f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, 0.55f, -0.15f), chamber);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.25f, 0.75f, -0.35f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -22f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -32f, strike);
        rig.Rotate(rig.Head, Vector3.up, 28f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(-0.10f, 0.15f, 0.95f), strike);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.30f, -0.05f, 0.90f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.40f, -0.55f, -0.40f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.04f, 0.08f), strike);
    }

    public static void Kick(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        float chamber = Pulse(u, 0.00f, 0.22f, 0.28f, 0.50f);
        float strike = Pulse(u, 0.26f, 0.46f, 0.60f, 1.00f);

        // Chamber: the knee spears up across the body, hips pre-load the
        // twist, arms flare wide for balance, the support leg sits deeper.
        rig.Rotate(rig.Hips, Vector3.up, 16f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 12f, chamber);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(-0.15f, 0.55f, 0.55f), chamber);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.85f, -0.50f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.06f, 0f), chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.70f, 0.10f, -0.30f), chamber * 0.8f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.60f, 0.25f, 0.30f), chamber * 0.8f);

        // Strike: the roundhouse — hips whip through the other way, torso
        // counter-twists and tips off the kick, the shin snaps out high,
        // the arms sweep against the leg, eyes never leave the target.
        rig.Rotate(rig.Hips, Vector3.up, -30f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -22f, strike);
        rig.Rotate(rig.Head, Vector3.up, 34f, strike);
        rig.Rotate(rig.Chest, Vector3.forward, 10f, strike);
        rig.Rotate(rig.Hips, Vector3.right, -10f, strike);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.05f, 0.50f, 0.88f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.18f, 1f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, -0.35f, -0.65f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.35f, 0.45f, 0.55f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.06f), strike);
    }

    public static void KickHigh(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The roundhouse's arc, aimed at the head: more lean, more height.
        float chamber = Pulse(u, 0.00f, 0.22f, 0.28f, 0.50f);
        float strike = Pulse(u, 0.26f, 0.46f, 0.60f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 16f, chamber);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(-0.1f, 0.7f, 0.45f), chamber);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.8f, -0.55f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.07f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -28f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -20f, strike);
        rig.Rotate(rig.Hips, Vector3.right, -16f, strike);
        rig.Rotate(rig.Head, Vector3.up, 30f, strike);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.05f, 0.8f, 0.55f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.55f, 0.85f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.6f, -0.4f, -0.55f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.35f, 0.5f, 0.5f), strike);
    }

    public static void KickSide(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Linear: knee chambers straight, the heel drives THROUGH, torso
        // blades hard away for the reach.
        float chamber = Pulse(u, 0.00f, 0.20f, 0.26f, 0.48f);
        float strike = Pulse(u, 0.24f, 0.44f, 0.60f, 1.00f);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.65f, 0.6f), chamber);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.9f, -0.4f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.06f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, 24f, strike);          // blades away
        rig.Rotate(rig.Chest, Vector3.up, 20f, strike);
        rig.Rotate(rig.Hips, Vector3.right, -14f, strike);
        rig.Rotate(rig.Head, Vector3.up, -36f, strike);         // eyes stay front
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.35f, 0.95f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.25f, 1f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.7f, -0.3f, -0.5f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.55f, 0.3f, 0.35f), strike);
    }

    public static void KickLow(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The sweep: sink onto the support leg and scythe at shin height.
        float chamber = Pulse(u, 0.00f, 0.20f, 0.26f, 0.48f);
        float strike = Pulse(u, 0.24f, 0.44f, 0.60f, 1.00f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.16f, 0f), Mathf.Max(chamber, strike));
        rig.Rotate(rig.Hips, Vector3.up, 18f, chamber);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(-0.2f, 0.2f, 0.5f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -30f, strike);
        rig.Rotate(rig.Chest, Vector3.up, -18f, strike);
        rig.Rotate(rig.Chest, Vector3.right, 12f, strike);      // folds over the sweep
        rig.Rotate(rig.Head, Vector3.up, 28f, strike);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.1f, -0.1f, 0.95f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0.05f, -0.2f, 1f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.6f, 0.15f, -0.4f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.4f, 0.45f, 0.4f), strike);
    }

    public static void KickSpin(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The whole frame whips through a big yaw and the leg rides it out.
        float chamber = Pulse(u, 0.00f, 0.20f, 0.24f, 0.46f);
        float strike = Pulse(u, 0.22f, 0.44f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 30f, chamber);         // deep wind-up
        rig.Rotate(rig.Chest, Vector3.up, 22f, chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.08f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, -55f, strike);         // the spin
        rig.Rotate(rig.Chest, Vector3.up, -30f, strike);
        rig.Rotate(rig.Head, Vector3.up, 55f, strike);
        rig.Rotate(rig.Hips, Vector3.right, -12f, strike);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(-0.25f, 0.5f, 0.8f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(-0.15f, 0.3f, 1f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.75f, 0.1f, -0.35f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.7f, 0.2f, -0.25f), strike);
    }

    public static void KickAxe(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The leg swings straight up past the target's guard, then the heel
        // drops like a guillotine while the trunk crunches down after it.
        float chamber = Pulse(u, 0.00f, 0.22f, 0.28f, 0.50f);
        float strike = Pulse(u, 0.26f, 0.46f, 0.60f, 1.00f);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.02f, 0.80f, 0.55f), chamber);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.55f, 0.80f), chamber);
        rig.Rotate(rig.Chest, Vector3.right, -12f, chamber);   // leans off the rise
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.45f, 0.35f, -0.30f), chamber * 0.8f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.45f, 0.35f, -0.30f), chamber * 0.8f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.04f, 0f), chamber);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.02f, 0.10f, 0.95f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.50f, 0.80f), strike);
        rig.Rotate(rig.Chest, Vector3.right, 18f, strike);     // rides the chop down
        rig.Rotate(rig.Hips, Vector3.up, -10f, strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.50f, -0.40f, -0.30f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.45f, -0.35f, 0.25f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.08f, 0.08f), strike);
    }

    public static void KickCrescent(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The LEFT leg for once: it lifts across the centreline and sweeps
        // an outward arc through head height, arms fanning against it.
        float chamber = Pulse(u, 0.00f, 0.22f, 0.28f, 0.50f);
        float strike = Pulse(u, 0.26f, 0.46f, 0.60f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, -18f, chamber);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(0.30f, 0.55f, 0.55f), chamber);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0.15f, -0.60f, -0.50f), chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, 0.30f, 0.25f), chamber * 0.8f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.50f, 0.15f, -0.35f), chamber * 0.8f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.06f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, 26f, strike);
        rig.Rotate(rig.Chest, Vector3.up, 20f, strike);
        rig.Rotate(rig.Head, Vector3.up, -30f, strike);        // eyes stay front
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.30f, 0.60f, 0.70f), strike);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(-0.20f, 0.30f, 0.90f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.55f, -0.30f, -0.40f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.35f, 0.40f, 0.45f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0.06f), strike);
    }

    public static void KickBack(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // The mule kick: the body turns AWAY, eyes finding the target over
        // the shoulder, torso tipping off it while the heel piston-drives
        // straight back into the opponent.
        float chamber = Pulse(u, 0.00f, 0.20f, 0.26f, 0.48f);
        float strike = Pulse(u, 0.24f, 0.44f, 0.60f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 30f, chamber);
        rig.Rotate(rig.Chest, Vector3.up, 22f, chamber);
        rig.Rotate(rig.Head, Vector3.up, -40f, chamber);       // over the shoulder
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.05f, 0.55f, 0.30f), chamber);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.85f, -0.35f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.07f, 0f), chamber);
        rig.Rotate(rig.Hips, Vector3.up, 32f, strike);          // keeps turning
        rig.Rotate(rig.Chest, Vector3.up, 18f, strike);
        rig.Rotate(rig.Chest, Vector3.right, -22f, strike);     // tips away
        rig.Rotate(rig.Head, Vector3.up, -15f, strike);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(-0.05f, 0.10f, 0.95f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.15f, 0.98f), strike);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, 0.25f, -0.55f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.45f, 0.30f, -0.35f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, 0.10f), strike);
    }

    public static void KickKnee(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        // Muay thai: the hands reach and RIP down while the knee spears up
        // through the middle, hips thrusting in behind it.
        float chamber = Pulse(u, 0.00f, 0.18f, 0.24f, 0.46f);
        float strike = Pulse(u, 0.22f, 0.42f, 0.58f, 1.00f);
        rig.Rotate(rig.Hips, Vector3.up, 8f, chamber);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.20f, 0.25f, 0.85f), chamber);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.05f, 0.10f, 0.95f), chamber);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.20f, 0.25f, 0.85f), chamber);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.05f, 0.10f, 0.95f), chamber);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, 0f), chamber);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.60f, 0.75f), strike);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.90f, -0.30f), strike);   // shin folded hard
        rig.Rotate(rig.Chest, Vector3.right, 18f, strike);     // crunches onto it
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.40f, -0.55f, -0.30f), strike);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.40f, -0.55f, -0.30f), strike);
        rig.Shift(rig.Hips, new Vector3(0f, 0.02f, 0.14f), strike);
    }

    public static void FlyKick(BrawlPoseRig rig, float u)
    {
        // The stance dissolves as the ground does.
        StanceBase(rig, Mathf.Clamp01(1f - u * 3f));
        float coil = Pulse(u, 0.00f, 0.16f, 0.20f, 0.42f);
        float extend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.20f) / 0.22f));

        // Coil: both knees tuck, arms cross over the chest, body curls.
        rig.Rotate(rig.Chest, Vector3.right, 10f, coil);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.55f, 0.55f), coil);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(0f, 0.45f, 0.35f), coil);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.55f, 0.60f, 0.30f), coil);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.55f, 0.60f, 0.30f), coil);

        // Extension: the spear — lead leg locks out, trail leg folds back,
        // torso sails back and twists, arms fling opposite corners. Held
        // until landing; the exit blend unwinds it.
        rig.Rotate(rig.Hips, Vector3.right, -22f, extend);
        rig.Rotate(rig.Hips, Vector3.up, -14f, extend);
        rig.Rotate(rig.Chest, Vector3.up, -20f, extend);
        rig.Rotate(rig.Head, Vector3.up, 26f, extend);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0f, 0.25f, 1f), extend);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, 0.18f, 1f), extend);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(0f, -0.35f, -0.80f), extend);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -1f, -0.35f), extend);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, 0.65f, -0.45f), extend);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.70f, -0.15f, 0.35f), extend);
    }

    // ------------------------------------------------------------- the leap
    //
    // The jump is three templates rather than one, because how long a robot
    // is off the ground is decided by physics, not by a clip: CharacterMotor
    // throws it at whatever height the rule asks for and RobotJump's arc is
    // as long as the gap it crosses. So the drive plays once, the tuck holds
    // for as long as the flight actually lasts, and the landing plays once.
    // A single fixed-length leap clip would either end in mid-air or still
    // be taking off after the feet were back down.
    //
    // These stand on the RIG'S REST POSE, not on StanceBase: the Gunfight
    // robot is carrying a blaster and blends out of a walk cycle, so a
    // fighter's bladed guard would be the wrong thing to land back into.

    /// <summary>
    /// The airborne shape, shared so the landing can begin in EXACTLY what
    /// the flight was holding: lead knee speared up with the shin folded
    /// hard under it, trail leg swept back, rear fist chambered at the hip
    /// and the lead knife-hand out — a jumping knee, not a ragdoll.
    /// </summary>
    public static void JumpTuck(BrawlPoseRig rig, float w)
    {
        if (w <= 0f)
            return;
        rig.Rotate(rig.Hips, Vector3.up, 10f, w);
        rig.Rotate(rig.Chest, Vector3.right, 9f, w);      // crunches over the knee
        rig.Rotate(rig.Head, Vector3.up, -12f, w);        // eyes stay downrange

        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.02f, 0.55f, 0.62f), w);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.88f, -0.32f), w);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.06f, -0.55f, -0.62f), w);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -0.85f, -0.38f), w);

        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.40f, -0.60f, -0.40f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.20f, -0.15f, -0.85f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.30f, 0.35f, 0.75f), w);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.10f, 0.45f, 0.85f), w);
    }

    /// <summary>
    /// The drive, played the instant the feet leave the floor. There is no
    /// anticipation frame in it on purpose — the motor has already launched
    /// by the time this state is entered, so a crouch here would be a robot
    /// squatting in mid-air. What survives of the push-off is the tail of
    /// the extension: legs snapping straight and trailing, both arms
    /// swinging up through the takeoff, chest and chin opening to the sky.
    /// </summary>
    public static void JumpLaunch(BrawlPoseRig rig, float u)
    {
        float drive = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.55f));
        float snap = Pulse(u, 0.00f, 0.10f, 0.14f, 0.40f);   // the last of the push

        // Legs finish the extension and trail behind the body.
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.05f, -0.94f, -0.28f), drive);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -0.98f, -0.18f), drive);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.05f, -0.95f, -0.18f), drive);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -0.98f, -0.12f), drive);

        // Both arms swing up — the swing is where a jump gets its height.
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.28f, 0.85f, 0.32f), drive);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.14f, 0.94f, 0.22f), drive);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.28f, 0.85f, 0.32f), drive);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.14f, 0.94f, 0.22f), drive);

        rig.Rotate(rig.Chest, Vector3.right, -13f, drive);   // opens up off the floor
        rig.Rotate(rig.Head, Vector3.right, -8f, drive);
        rig.Shift(rig.Hips, new Vector3(0f, 0.05f, 0f), drive);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f, 0f), snap);
    }

    /// <summary>
    /// The flight, looped for however long the robot is actually up there.
    /// First and last frames match — every oscillation is a full sine.
    /// </summary>
    public static void JumpAir(BrawlPoseRig rig, float u)
    {
        JumpTuck(rig, 1f);
        float sway = Mathf.Sin(u * 2f * Mathf.PI);
        rig.Rotate(rig.Chest, Vector3.up, 4f * sway, 1f);
        rig.Rotate(rig.Hips, Vector3.up, -3f * sway, 1f);
        rig.Shift(rig.Hips, new Vector3(0f, 0.018f * sway, 0f), 1f);
        // The speared knee breathes rather than hanging frozen.
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.02f, 0.62f, 0.58f), 0.12f + 0.10f * sway);
    }

    /// <summary>
    /// The landing: the tuck unwinds, the feet reach down and plant, the
    /// whole frame sinks into an absorbing crouch and rises back out of it.
    /// Ends on the rest pose exactly, which is what the walk blend tree is
    /// expecting to take back.
    /// </summary>
    public static void JumpLand(BrawlPoseRig rig, float u)
    {
        float tuck = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.30f));
        float absorb = Pulse(u, 0.14f, 0.34f, 0.48f, 1.00f);

        JumpTuck(rig, tuck);

        // Knees eat the landing, torso folds over them, eyes come up first.
        rig.Shift(rig.Hips, new Vector3(0f, -0.22f, 0f), absorb);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.10f, -0.55f, 0.62f), absorb);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -1f, -0.30f), absorb);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.10f, -0.58f, 0.55f), absorb);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -1f, -0.24f), absorb);
        rig.Rotate(rig.Chest, Vector3.right, 17f, absorb);
        rig.Rotate(rig.Head, Vector3.right, -15f, absorb);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, -0.45f, 0.35f), absorb);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.30f, -0.20f, 0.85f), absorb);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.55f, -0.45f, 0.35f), absorb);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.30f, -0.20f, 0.85f), absorb);
    }

    public static void Block(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.35f));
        float bob = 0.012f * Mathf.Sin(u * 2f * Mathf.PI);

        // The guard turns INTO the pressure: lead shoulder blades in, the
        // forearms cross the centreline, the whole frame sits down on it.
        rig.Rotate(rig.Hips, Vector3.up, 8f, w);
        rig.Rotate(rig.Chest, Vector3.up, -20f, w);
        rig.Rotate(rig.Head, Vector3.up, 12f, w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(-0.10f, 0.20f, 0.75f), w);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(-0.30f, 0.85f, 0.35f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(0.10f, 0.20f, 0.75f), w);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(0.30f, 0.85f, 0.35f), w);
        rig.Rotate(rig.Chest, Vector3.right, 7f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.05f + bob, 0f), w);
    }

    public static void Hit(BrawlPoseRig rig, float u)
    {
        StanceBase(rig, 1f);
        float w = u < 0.2f
            ? Mathf.SmoothStep(0f, 1f, u / 0.2f)
            : Mathf.SmoothStep(1f, 0f, (u - 0.2f) / 0.8f);

        // The head snaps first, the torso wrenches around after it, the
        // hips give ground and the arms fly loose of the guard.
        rig.Rotate(rig.Chest, Vector3.right, -14f, w);
        rig.Rotate(rig.Chest, Vector3.up, 18f, w);
        rig.Rotate(rig.Hips, Vector3.up, 10f, w);
        rig.Rotate(rig.Head, Vector3.right, -11f, w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.55f, -0.35f, -0.30f), w * 0.5f);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.60f, -0.20f, 0.15f), w * 0.5f);
        rig.Shift(rig.Hips, new Vector3(0f, -0.03f, 0f), w);   // the stagger is the root's knockback
    }

    /// <summary>
    /// The crumpled sprawl a knockdown ends in — shared so the rise can
    /// begin in EXACTLY the shape the fall finished (no pop at the seam)
    /// and the KO state can hold it. Hips tipped ~58° with the knees folded
    /// under, not the 90° stiff plank of a felled tree.
    /// </summary>
    public static void FallPose(BrawlPoseRig rig, float w)
    {
        if (w <= 0f)
            return;
        // No Z here on purpose: like every clip in the project, the fall
        // plays in place and BrawlFighter's knockback slide owns the travel
        // — in-clip travel is exactly what made robots stand up somewhere
        // other than where they landed.
        rig.Rotate(rig.Hips, Vector3.right, -58f, w);
        rig.Shift(rig.Hips, new Vector3(0f, -0.66f, 0f), w);
        rig.Rotate(rig.UpLegR, Vector3.right, 46f, w);
        rig.Rotate(rig.UpLegL, Vector3.right, 38f, w);
        rig.Rotate(rig.LegR, Vector3.right, -34f, w);
        rig.Rotate(rig.LegL, Vector3.right, -26f, w);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.85f, 0.05f, -0.45f), w);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.85f, 0.05f, -0.45f), w);
        rig.Rotate(rig.Chest, Vector3.right, -10f, w);
        rig.Rotate(rig.Head, Vector3.right, 14f, w);   // chin tucked, not lolled flat
    }

    public static void Knockdown(BrawlPoseRig rig, float u)
    {
        float whiplash = Pulse(u, 0.00f, 0.10f, 0.16f, 0.45f);
        float fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.18f) / 0.62f));
        StanceBase(rig, 1f - fall);

        // The hit arrives first: head and chest snap back, the arms fling
        // up, the whole frame is thrown rearward — the body REACTS before
        // gravity gets a say.
        rig.Rotate(rig.Head, Vector3.right, -26f, whiplash);
        rig.Rotate(rig.Chest, Vector3.right, -18f, whiplash);
        rig.Rotate(rig.Chest, Vector3.up, 12f, whiplash);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.35f, 0.75f, 0.30f), whiplash);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.45f, 0.65f, 0.25f), whiplash);
        rig.Shift(rig.Hips, new Vector3(0f, 0.02f, 0f), whiplash);

        // Then the legs give and the body crumples down and backward into
        // the sprawl the get-up starts from.
        FallPose(rig, fall);
    }

    public static void GetUp(BrawlPoseRig rig, float u)
    {
        // Nobody hinges upright. The floor pose unwinds while the knees
        // gather underneath, the body passes through a deep crouch — torso
        // folded over the feet, arms pushing off — and only then does the
        // stance rise out of it.
        float lying = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.45f));
        float crouch = Pulse(u, 0.20f, 0.50f, 0.62f, 0.95f);
        float stance = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.55f) / 0.45f));

        StanceBase(rig, stance);
        FallPose(rig, lying);

        rig.Shift(rig.Hips, new Vector3(0f, -0.34f, 0f), crouch);
        rig.Rotate(rig.Hips, Vector3.right, 24f, crouch);
        rig.Rotate(rig.Chest, Vector3.right, 18f, crouch);
        rig.Aim(rig.UpLegR, rig.LegR, new Vector3(0.05f, -0.55f, 0.65f), crouch);
        rig.Aim(rig.UpLegL, rig.LegL, new Vector3(-0.05f, -0.60f, 0.55f), crouch);
        rig.Aim(rig.LegR, rig.FootR, new Vector3(0f, -1f, -0.30f), crouch);
        rig.Aim(rig.LegL, rig.FootL, new Vector3(0f, -1f, -0.25f), crouch);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.30f, -0.80f, 0.25f), crouch);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.30f, -0.80f, 0.25f), crouch);
        rig.Rotate(rig.Head, Vector3.right, -20f, crouch);   // eyes come up first
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
        StanceBase(rig, 1f);
        float gather = Pulse(u, 0.00f, 0.24f, 0.30f, 0.52f);
        float thrust = Pulse(u, 0.30f, 0.46f, 0.68f, 1.00f);

        // Gather: arms sweep wide and back, the frame sinks deep and the
        // chest coils away — the energy visibly collects before it leaves.
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.75f, -0.20f, -0.50f), gather);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.55f, -0.10f, -0.70f), gather);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.75f, -0.20f, -0.50f), gather);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.55f, -0.10f, -0.70f), gather);
        rig.Rotate(rig.Chest, Vector3.right, -10f, gather);
        rig.Shift(rig.Hips, new Vector3(0f, -0.12f, -0.08f), gather);

        // Thrust: both palms drive the bolt out, the hips ride forward and
        // up behind it, shoulders square to the target.
        rig.Rotate(rig.Hips, Vector3.up, -12f, thrust);
        rig.Rotate(rig.Chest, Vector3.up, -8f, thrust);
        rig.Rotate(rig.Chest, Vector3.right, 8f, thrust);
        rig.Aim(rig.ArmR, rig.ForeArmR, new Vector3(0.18f, 0.08f, 1f), thrust);
        rig.Aim(rig.ForeArmR, rig.HandR, new Vector3(0.10f, 0.05f, 1f), thrust);
        rig.Aim(rig.ArmL, rig.ForeArmL, new Vector3(-0.18f, 0.08f, 1f), thrust);
        rig.Aim(rig.ForeArmL, rig.HandL, new Vector3(-0.10f, 0.05f, 1f), thrust);
        rig.Shift(rig.Hips, new Vector3(0f, 0.04f, 0.16f), thrust);
    }
}
