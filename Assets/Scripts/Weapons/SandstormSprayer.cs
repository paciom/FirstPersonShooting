using UnityEngine;

/// <summary>
/// Abrasive wind: a close-range cone of swirling golden grit that scours
/// shields and blinds enemy aim while they're caught in the storm.
/// </summary>
public class SandstormSprayer : Weapon
{
    public float coneRange = 8f;
    public float coneDot = 0.6f;
    public float damagePerSecond = 16f;

    float _nextTick;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Sandstorm Sprayer";
        color = new Color(1f, 0.85f, 0.45f);
        range = coneRange;
        preferredRange = 6f;
    }

    public override void TryFire(Vector3 direction)
    {
        Vector3 dir = direction.normalized;

        // Golden grit spraying from the muzzle every frame it's held.
        VfxUtil.SpawnBurst(muzzle.position + dir * 1f, color, 3, 7f, 0.08f);
        FlashMuzzle(2.5f);

        if (Time.time < _nextTick)
            return;
        _nextTick = Time.time + 0.25f;

        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            Vector3 to = WeaponUtil.Center(shield) - muzzle.position;
            if (to.magnitude > coneRange || Vector3.Dot(to.normalized, dir) < coneDot)
                continue;
            shield.TakeHit(damagePerSecond * 0.25f, WeaponUtil.Center(shield), ownerRoot);
            StatusEffects.Get(shield.transform.root)?.ApplyBlind(1.5f);
            // Quartz glints swirling around the victim.
            VfxUtil.SpawnBurst(WeaponUtil.Center(shield), color, 4, 2f, 0.07f);
        }
    }
}
