using UnityEngine;

/// <summary>
/// Kinetic + electric: hurls a spinning electrified blade that arcs out,
/// flips around in a flash, and returns to your hand — zapping everyone it
/// passes on both legs of the trip.
/// </summary>
public class VoltBoomerang : Weapon
{
    public float throwsPerSecond = 0.9f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Volt Boomerang";
        color = new Color(0.3f, 0.75f, 1f);
        if (damage == 20f) damage = 18f;
        preferredRange = 14f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / throwsPerSecond;

        // The blade flips around at whatever the crosshair is on — ray-pick
        // the turnaround distance (capped, and with a minimum so a wall in
        // your face doesn't make it flip instantly).
        float turnDistance = 24f;
        var hits = Physics.RaycastAll(muzzle.position, direction.normalized, 24f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.root == ownerRoot)
                continue;
            turnDistance = Mathf.Max(4f, hit.distance);
            break;
        }

        BoomerangEntity.Spawn(muzzle.position, direction.normalized, damage, TeamId, ownerRoot, color, turnDistance);
        FlashMuzzle(3.5f);
    }
}
