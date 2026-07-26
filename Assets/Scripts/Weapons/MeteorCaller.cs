using System.Collections;
using UnityEngine;

/// <summary>
/// Gravity well: paint a red target circle on the ground; two beats later a
/// glowing meteor screams down from the sky and craters the spot.
/// </summary>
public class MeteorCaller : Weapon
{
    public float callsPerSecond = 0.3f;
    public float warningSeconds = 1.5f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Meteor Caller";
        color = new Color(1f, 0.35f, 0.2f);
        if (damage == 20f) damage = 45f;
        preferredRange = 28f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / callsPerSecond;

        Vector3 target = muzzle.position + direction.normalized * range;
        if (Physics.Raycast(muzzle.position, direction.normalized, out RaycastHit hit, range,
                ~0, QueryTriggerInteraction.Ignore) && hit.transform.root != ownerRoot)
            target = hit.point;

        // Snap the mark to the floor.
        if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 40f,
                ~0, QueryTriggerInteraction.Ignore))
            target = ground.point;

        StartCoroutine(CallMeteor(target));
        FlashMuzzle(4f);
    }

    IEnumerator CallMeteor(Vector3 target)
    {
        // Red target circle — the "get out of there" warning.
        var mark = WeaponUtil.GhostShell(PrimitiveType.Cylinder, target + Vector3.up * 0.05f,
            new Vector3(4.5f, 0.02f, 4.5f), color, 0.8f);
        Destroy(mark, warningSeconds);

        yield return new WaitForSeconds(warningSeconds);

        // The meteor drops from high above, slightly angled for drama.
        Vector3 from = target + new Vector3(Random.Range(-3f, 3f), 26f, Random.Range(-3f, 3f));
        var spec = new BoltSpec
        {
            speed = 34f,
            damage = damage,
            color = color,
            shape = PrimitiveType.Sphere,
            size = 0.9f,
            glow = 2.2f,        // orange rock; the white heat is the heart child
            trailTime = 0.4f,   // slim streak — embers carry the fire tail
            trailWidth = 0.28f,
            trailGlow = 1.4f,
            splashRadius = 4.5f,
            splashScale = 2f,
            lifetime = 3f,
        };
        var meteor = GenericBolt.Spawn(from, (target - from).normalized, spec, TeamId, ownerRoot);
        WeaponUtil.DressAsFireball(meteor, color);
    }
}
