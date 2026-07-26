using UnityEngine;

/// <summary>
/// Wind vortex: releases a mini tornado that wanders across the arena,
/// scooping up enemies and pinwheeling them through the air.
/// </summary>
public class TornadoTube : Weapon
{
    public float cooldownSeconds = 4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Tornado Tube";
        color = new Color(0.65f, 0.9f, 0.75f);
        if (damage == 20f) damage = 12f;   // per second inside the funnel
        preferredRange = 14f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + cooldownSeconds;

        Vector3 start = ownerRoot.position + direction.normalized * 2f;
        start.y = ownerRoot.position.y;
        var tornado = TornadoEntity.Spawn(start, direction, TeamId, ownerRoot, color);
        tornado.damagePerSecond = damage;
        FlashMuzzle(4f);
    }
}
