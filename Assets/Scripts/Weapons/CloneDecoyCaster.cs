using UnityEngine;

/// <summary>
/// Hard light: projects a flickering hologram of a fighter that jogs forward
/// soaking up enemy fire, then pixel-pops when its light runs out.
/// </summary>
public class CloneDecoyCaster : Weapon
{
    public float cooldownSeconds = 7f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Clone Decoy Caster";
        color = new Color(0.4f, 0.95f, 1f);
        preferredRange = 14f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + cooldownSeconds;

        Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
        Vector3 spawnAt = ownerRoot.position + flat * 1.4f;
        DecoyEntity.Spawn(spawnAt, flat, TeamId, color);
        VfxUtil.Explosion(spawnAt + Vector3.up * 1.2f, color, 0.7f);
        FlashMuzzle(4.5f);
    }
}
