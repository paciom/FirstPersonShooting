using UnityEngine;

/// <summary>
/// Sonar: a green ping sweeps the arena, lighting every enemy up as a
/// silhouette through walls — and the echo itself stings whoever it finds.
/// </summary>
public class EchoLocator : Weapon
{
    public float pingsPerSecond = 0.5f;
    public float pingRadius = 28f;
    public float revealSeconds = 3f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Echo Locator";
        color = new Color(0.3f, 1f, 0.5f);
        if (damage == 20f) damage = 8f;
        preferredRange = 22f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / pingsPerSecond;

        // Expanding sonar sphere.
        VfxUtil.EnergyBurst(ownerRoot.position + Vector3.up * 1.2f, color, 2f);

        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            if (Vector3.Distance(shield.transform.position, ownerRoot.position) > pingRadius)
                continue;
            // The ping finds them through everything — walls included.
            StatusEffects.Get(shield.transform.root)?.ApplyReveal(revealSeconds, color);
            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            VfxUtil.ImpactBurst(WeaponUtil.Center(shield), color);
        }
        FlashMuzzle(4f);
    }
}
