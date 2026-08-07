using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the hero has captured, and what that is worth.
///
/// Boons are the run's PROGRESSION. Pods are borrowed for twenty seconds and
/// lost on the next de-rez; a boon is kept for the whole run, and it stacks. A
/// player twelve hundred metres in with a reactor, two upgrades and a seized
/// factory is driving a visibly different tank from the one they started in —
/// which is the point of putting outposts on the field at all.
///
/// STORED AS FACTORS, APPLIED FROM BASE. <see cref="Apply"/> recomputes each gun
/// and the shield from the numbers they were built with rather than multiplying
/// whatever they are holding now, because it is re-run on every respawn and
/// after every capture. Multiplying in place would compound the same upgrade
/// once per continue, and a mode with three lives would quietly hand out three
/// times the reward for one outpost.
/// </summary>
public class TankBoons
{
    public float damageScale = 1f;
    public float rateScale = 1f;
    public float shieldBonus;
    public float regenScale = 1f;

    /// <summary>Extra recruits the hero may keep at once.</summary>
    public int allyCapBonus;

    /// <summary>Captured names, oldest first — the HUD's trophy shelf.</summary>
    public readonly List<string> Taken = new List<string>();

    /// <summary>
    /// Stamp the current totals onto the hero.
    ///
    /// Safe to call as often as you like; that is the whole design. Call it
    /// after every capture and after every continue, and never worry about
    /// which of the two happened last.
    /// </summary>
    public void Apply(TankPawn hero)
    {
        if (hero == null)
            return;

        if (hero.Shield != null)
        {
            float wanted = TankRaid.HeroShield + shieldBonus;
            // Raised, then topped up by the difference: a capture that widens the
            // tank should not also silently heal the damage it took getting
            // there, but neither should it leave a bar that can never fill.
            float headroom = wanted - hero.Shield.maxShield;
            hero.Shield.maxShield = wanted;
            if (headroom > 0f)
                hero.Shield.Restore(headroom);
            hero.Shield.regenPerSecond = TankRaid.HeroRegenPerSecond * regenScale;
        }

        hero.RestampGuns(damageScale, rateScale);
    }

    /// <summary>Take a reward and say what it was called.</summary>
    public void Add(TankOutpost.Reward reward)
    {
        damageScale *= reward.damageScale;
        rateScale *= reward.rateScale;
        shieldBonus += reward.shieldBonus;
        regenScale *= reward.regenScale;
        allyCapBonus += reward.allyCapBonus;
        Taken.Add(reward.title);
    }

    /// <summary>The trophy shelf as one line, newest last. Empty until something is taken.</summary>
    public string Summary => Taken.Count == 0 ? "" : string.Join("  ·  ", Taken);
}
