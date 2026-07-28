using UnityEngine;

/// <summary>
/// Chained lightning: a close-range electric whip that snaps to the nearest
/// enemy in front of you, then arcs on to two more nearby targets.
/// </summary>
public class ArcWhip : Weapon
{
    public float cracksPerSecond = 1.6f;
    public float whipRange = 9f;
    public int chainHops = 2;
    public float hopRange = 7f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Arc Whip";
        color = new Color(0.4f, 0.7f, 1f);
        if (damage == 20f) damage = 18f;
        preferredRange = 7f;
        range = whipRange;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / cracksPerSecond;

        // Find the nearest enemy roughly in front of the whip.
        EnergyShield best = null;
        float bestDist = whipRange;
        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            Vector3 to = WeaponUtil.Center(shield) - muzzle.position;
            if (Vector3.Dot(to.normalized, direction.normalized) < 0.35f)
                continue;
            if (to.magnitude < bestDist)
            {
                bestDist = to.magnitude;
                best = shield;
            }
        }

        if (best != null)
        {
            WeaponUtil.ChainLightning(muzzle.position, best, damage, chainHops, hopRange, TeamId, ownerRoot, color);
        }
        else
        {
            // Whip-crack into empty air — arc flash so misses still look electric.
            Vector3 end = muzzle.position + direction.normalized * whipRange * 0.7f;
            WeaponUtil.LightningArc(muzzle.position, end, color, 0.12f);
        }
        FlashMuzzle(4.5f);
    }
}
