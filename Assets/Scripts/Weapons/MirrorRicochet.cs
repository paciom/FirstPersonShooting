using UnityEngine;

/// <summary>
/// Coherent light: an instant silver laser that bounces up to five times off
/// walls, drawing its whole geometric path with a mirror-flash at each bounce.
/// </summary>
public class MirrorRicochet : Weapon
{
    public float shotsPerSecond = 1.4f;
    public int maxBounces = 5;
    float _nextFireTime;

    static readonly Color HaloBlue = new Color(0.45f, 0.7f, 1f);

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Mirror Ricochet";
        color = new Color(0.7f, 0.85f, 1f);
        if (damage == 20f) damage = 24f;
        preferredRange = 26f;
    }

    /// <summary>
    /// Two-layer beam segment: thin bright core inside a wide icy-blue halo —
    /// reads as polished chrome instead of a flat white bar.
    /// </summary>
    static void DrawSegment(Vector3 from, Vector3 to, float life)
    {
        FadingLine.Spawn(from, to, Color.white, 0.03f, life, 2.6f);
        FadingLine.Spawn(from, to, HaloBlue, 0.16f, life * 1.15f, 1.1f);
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;
        float remaining = range;

        for (int bounce = 0; bounce <= maxBounces && remaining > 0.5f; bounce++)
        {
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, remaining, ~0, QueryTriggerInteraction.Ignore))
            {
                DrawSegment(origin, origin + dir * remaining, 0.3f);
                break;
            }
            if (hit.transform.root == ownerRoot)
            {
                origin = hit.point + dir * 0.1f;
                continue;
            }

            // Later segments linger longer — the whole geometry diagram stays
            // readable for a beat after the shot.
            DrawSegment(origin, hit.point, 0.3f + bounce * 0.08f);

            var shield = hit.transform.root.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId != TeamId)
            {
                ApplyHit(hit, damage);
                break;   // the beam ends in the target
            }
            WeaponUtil.DamageProp(hit.collider, damage * 0.3f, hit.point);

            // Mirror flash at the bounce point, then reflect onward.
            VfxUtil.ImpactBurst(hit.point + hit.normal * 0.08f, HaloBlue);
            remaining -= hit.distance;
            origin = hit.point + hit.normal * 0.03f;
            dir = Vector3.Reflect(dir, hit.normal);
        }

        FlashMuzzle(4f);
    }
}
