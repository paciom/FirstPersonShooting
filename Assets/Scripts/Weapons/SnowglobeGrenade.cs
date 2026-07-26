using UnityEngine;

/// <summary>
/// Flash-freeze: lobs an orb that blooms into a glass-looking dome of gently
/// falling snow — everyone caught inside trudges in slow motion.
/// </summary>
public class SnowglobeGrenade : Weapon
{
    public float shotsPerSecond = 0.28f;
    public float globeRadius = 5f;
    public float globeSeconds = 4f;
    float _nextFireTime;
    EffectZone _activeGlobe;   // one globe per shooter — stacked domes white out the screen

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Snowglobe Grenade";
        color = new Color(0.75f, 0.9f, 1f);
        if (damage == 20f) damage = 8f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        if (_activeGlobe != null)
            return;   // wait for the current globe to melt before throwing another
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        var spec = new BoltSpec
        {
            speed = 16f,
            gravity = -12f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.35f,
            glow = 2f,   // frosty blue orb, not a white ball
            onImpact = (bolt, hit) =>
            {
                var zone = EffectZone.Spawn(hit.point + Vector3.up * 0.3f, globeRadius, globeSeconds,
                    bolt.spec.color, ZoneParticles.Snow, bolt.teamId, bolt.ownerRoot);
                zone.affectAllTeams = true;   // the snow doesn't pick sides
                zone.onCharacterTick = (z, shield) =>
                    StatusEffects.Get(shield.transform.root)?.ApplySlow(0.45f, 0.45f);
                _activeGlobe = zone;          // cleared automatically when the zone destroys itself
            },
        };
        GenericBolt.Spawn(muzzle.position, (direction.normalized + Vector3.up * 0.25f).normalized,
            spec, TeamId, ownerRoot);
        FlashMuzzle(3f);
    }
}
