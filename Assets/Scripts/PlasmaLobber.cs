using UnityEngine;

/// <summary>
/// Lobs slow, arcing plasma orbs that burst for area damage. The launch
/// velocity is solved ballistically for the point under the crosshair: a low
/// flat toss up close, a high arc at range — so close targets no longer get
/// harmlessly overflown.
/// </summary>
public class PlasmaLobber : Weapon
{
    [Header("Plasma Lobber")]
    public float shotsPerSecond = 1.1f;
    public float splashRadius = 3.8f;

    [Tooltip("Furthest point the arc is solved for when the aim ray hits nothing.")]
    public float maxAimDistance = 26f;
    [Tooltip("Flight time scales with distance between these bounds — shapes the arc.")]
    public float minFlightTime = 0.35f;
    public float maxFlightTime = 1.3f;
    public float maxLaunchSpeed = 34f;

    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Plasma Lobber";
        if (damage == 20f) damage = 40f;   // hits harder but slow + area
        preferredRange = 22f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;

        _nextFireTime = Time.time + 1f / shotsPerSecond;
        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;

        // Find the point the shot should land on: first thing the aim ray hits
        // (skipping the shooter), or a far point along the ray.
        Vector3 target = origin + dir * maxAimDistance;
        var hits = Physics.RaycastAll(origin, dir, maxAimDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.root == ownerRoot)
                continue;
            target = hit.point;
            break;
        }

        // Ballistic solve: pick a flight time from the horizontal distance
        // (short = flat toss, long = high arc), then the velocity that lands
        // the orb on the target after exactly that time under orb gravity.
        Vector3 delta = target - origin;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float flightTime = Mathf.Clamp(flat.magnitude / 16f, minFlightTime, maxFlightTime);
        float g = -PlasmaOrb.Gravity;
        Vector3 velocity = flat / flightTime
            + Vector3.up * (delta.y / flightTime + 0.5f * g * flightTime);
        if (velocity.magnitude > maxLaunchSpeed)
            velocity = velocity.normalized * maxLaunchSpeed;   // falls short instead of exploding fast

        PlasmaOrb.Spawn(origin, velocity, damage, splashRadius, color, TeamId, ownerRoot);
        FlashMuzzle(3.5f);
    }
}
