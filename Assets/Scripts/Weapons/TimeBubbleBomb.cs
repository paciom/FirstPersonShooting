using UnityEngine;

/// <summary>
/// Chrono field: an amber dome where time itself runs slow — characters wade
/// and even projectiles crawl while crossing it.
/// </summary>
public class TimeBubbleBomb : Weapon
{
    public float shotsPerSecond = 0.35f;
    public float bubbleRadius = 5f;
    public float bubbleSeconds = 4f;
    public float slowTimeScale = 0.35f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Time Bubble Bomb";
        color = new Color(1f, 0.75f, 0.3f);
        if (damage == 20f) damage = 6f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 15f,
            gravity = -11f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.3f,
            glow = 1.8f,   // amber, not white
            trailTime = 0.25f,
            trailGlow = 1.5f,
            onImpact = (bolt, hit) =>
            {
                var zone = EffectZone.Spawn(hit.point + Vector3.up * 0.3f, bubbleRadius, bubbleSeconds,
                    bolt.spec.color, ZoneParticles.Sparkle, bolt.teamId, bolt.ownerRoot);
                zone.timeScale = slowTimeScale;      // projectiles crawl through
                zone.affectAllTeams = true;          // time doesn't pick sides
                zone.onCharacterTick = (z, shield) =>
                    StatusEffects.Get(shield.transform.root)?.ApplySlow(slowTimeScale, 0.45f);
            },
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.2f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }
}
