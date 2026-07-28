using UnityEngine;

/// <summary>
/// Magnetism: fires a magnet slug at a wall or floor; where it strikes, a
/// magnet field anchors for a few seconds, dragging nearby enemies toward it
/// and slowing them — red/blue polarity lines arcing from the victims into
/// the core. Area control, not a tractor beam.
/// </summary>
public class MagnetRam : Weapon
{
    public float cooldownSeconds = 3f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Magnet Ram";
        // Team-colored so everyone knows whose field to avoid.
        color = TeamId == 1 ? new Color(1f, 0.25f, 0.85f) : new Color(0.2f, 0.9f, 1f);
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + cooldownSeconds;

        var spec = new BoltSpec
        {
            speed = 45f,
            damage = 8f,
            color = color,
            size = 0.14f,
            glow = 2.2f,
            trailTime = 0.1f,
            onImpact = (bolt, hit) =>
                MagnetFieldEntity.Spawn(hit.point, hit.normal, bolt.teamId, bolt.ownerRoot, bolt.spec.color),
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(4f);
    }
}
