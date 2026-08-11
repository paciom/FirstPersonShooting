using UnityEngine;

/// <summary>
/// What actually happens when a robot touches a treasure, and what happens when
/// a Scrap Mine goes off. Kept out of <see cref="TreasureDrop"/> so the drop
/// only has to worry about falling, being shot, and being reached — adding a
/// new treasure kind is a row in <see cref="TreasureCatalog"/> plus a case here.
/// </summary>
public static class TreasureEffects
{
    public const float RepairAmount = 55f;
    public const int GoldPerBar = 35;
    public const float OvershieldAmount = 60f;
    public const float OvershieldSeconds = 20f;
    public const float TurboFactor = 1.65f;
    public const float TurboSeconds = 15f;
    public const float EmpRadius = 18f;
    public const float EmpFreezeSeconds = 2.5f;

    /// <summary>Grant <paramref name="def"/> to whoever walked into it, and call it on the overlay.</summary>
    public static void Apply(TreasureDef def, EnergyShield picker)
    {
        if (def == null || picker == null)
            return;

        // The Mystery Cube resolves to something else first — including, if the
        // dice are cruel, the mine going off in the finder's face.
        if (def.kind == TreasureKind.MysteryCube)
        {
            var rolled = TreasureCatalog.Roll(includeMystery: false);
            MatchAnnouncer.Say($"{MatchAnnouncer.CharacterName(picker.transform)} OPENED THE MYSTERY CUBE",
                $"…it was {rolled.displayName}!", def.color);
            if (rolled.hazard)
            {
                Detonate(picker.transform.position + Vector3.up * 0.8f, rolled);
                return;
            }
            Apply(rolled, picker);
            return;
        }

        Transform root = picker.transform;
        Color tint = def.color;
        string who = MatchAnnouncer.CharacterName(root);
        string detail;

        switch (def.kind)
        {
            case TreasureKind.WeaponPod:
            {
                var loadout = WeaponLoadout.Of(root);
                var weapon = loadout != null ? loadout.GrantRandom() : null;
                if (weapon == null)
                    return;   // nothing to give — leave the pod for someone who can use it
                tint = weapon.color;
                detail = $"{weapon.weaponName.ToUpperInvariant()} for {loadout.grantDuration:0} seconds";
                break;
            }

            case TreasureKind.RepairPack:
                picker.Restore(RepairAmount);
                detail = $"+{RepairAmount:0} shield";
                break;

            case TreasureKind.GoldBars:
                TeamBank.Add(picker.teamId, GoldPerBar);
                detail = $"+{GoldPerBar} gold — {MatchAnnouncer.TeamName(picker.teamId)} now has " +
                         $"{TeamBank.Gold(picker.teamId)}/{TeamBank.RobotCost}";
                break;

            case TreasureKind.Overshield:
                picker.AddOvershield(OvershieldAmount, OvershieldSeconds);
                detail = $"+{OvershieldAmount:0} bonus shield for {OvershieldSeconds:0}s";
                break;

            case TreasureKind.TurboCells:
                StatusEffects.Get(root)?.ApplyHaste(TurboFactor, TurboSeconds);
                detail = $"{(TurboFactor - 1f) * 100f:0}% faster for {TurboSeconds:0}s";
                break;

            case TreasureKind.EmpCharge:
                detail = $"{FireEmp(picker)} enemies frozen solid";
                break;

            default:
                detail = def.blurb;
                break;
        }

        VfxUtil.EnergyBurst(root.position + Vector3.up * 1.1f, tint, 0.8f);
        MatchAnnouncer.Say($"{who} GRABBED {def.displayName}", detail, tint);
    }

    /// <summary>Freeze every enemy of the picker inside the EMP radius. Returns how many.</summary>
    static int FireEmp(EnergyShield picker)
    {
        int hit = 0;
        Vector3 origin = picker.transform.position + Vector3.up * 1f;
        var color = TreasureCatalog.Get(TreasureKind.EmpCharge).color;

        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == picker.teamId || shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            if (Vector3.Distance(origin, shield.transform.position) > EmpRadius)
                continue;
            StatusEffects.Get(shield.transform)?.ApplyFreeze(EmpFreezeSeconds);
            WeaponUtil.LightningArc(origin, WeaponUtil.Center(shield), color, 0.3f);
            hit++;
        }
        return hit;
    }

    // ------------------------------------------------------------------ bombs

    public const float BlastRadius = 6f;
    public const float BlastDamage = 55f;

    /// <summary>
    /// Scrap Mine detonation. Unlike every weapon in the game this hurts
    /// EVERYONE in range — both teams and the cover blocks — which is what
    /// makes shooting one next to an enemy a real play and walking into one a
    /// real mistake.
    /// </summary>
    public static void Detonate(Vector3 position, TreasureDef def)
    {
        Color color = def != null ? def.color : new Color(1f, 0.22f, 0.16f);
        VfxUtil.EnergyBurst(position, color, 3.2f);
        VfxUtil.EnergyBurst(position, new Color(1f, 0.85f, 0.5f), 1.8f);

        int caught = 0;
        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            float distance = Vector3.Distance(position, WeaponUtil.Center(shield));
            if (distance > BlastRadius)
                continue;
            // attacker stays null: nobody gets score credit for a stray mine.
            shield.TakeHit(BlastDamage * (1f - distance / BlastRadius), WeaponUtil.Center(shield));
            StatusEffects.Get(shield.transform)?.AddImpulse(
                (shield.transform.position - position).normalized * 6f);
            caught++;
        }

        foreach (var col in Physics.OverlapSphere(position, BlastRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var block = col.GetComponentInParent<ArenaBlock>();
            if (block == null)
                continue;
            float distance = Vector3.Distance(position, block.transform.position);
            block.TakeHit(BlastDamage * Mathf.Clamp01(1f - distance / BlastRadius), position);
        }

        MatchAnnouncer.Say("SCRAP MINE DETONATED",
            caught == 0 ? "nobody was close enough — clean sweep"
                        : $"{caught} robot{(caught == 1 ? "" : "s")} caught in the blast",
            color);
    }
}
