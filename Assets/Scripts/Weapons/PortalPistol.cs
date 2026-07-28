using UnityEngine;

/// <summary>
/// Spatial fold: first shot plants a glowing portal on whatever you hit; after
/// that, your shots pour OUT of the portal at the nearest enemy — fire around
/// corners like a proper space wizard.
/// </summary>
public class PortalPistol : Weapon
{
    public float shotsPerSecond = 3f;
    float _nextFireTime;
    PortalEntity _portal;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Portal Pistol";
        color = new Color(0.3f, 0.9f, 1f);
        if (damage == 20f) damage = 14f;
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        if (_portal == null)
        {
            PlacePortal(direction.normalized);
            return;
        }

        // Fire from the portal at whoever is closest to IT.
        var target = WeaponUtil.NearestEnemy(_portal.transform.position, TeamId, 30f, ownerRoot);
        Vector3 exitDir = target != null
            ? (WeaponUtil.Center(target) - _portal.transform.position).normalized
            : _portal.transform.forward;

        var spec = new BoltSpec
        {
            speed = 38f,
            damage = damage,
            color = color,
            size = 0.1f,
            trailTime = 0.09f,   // short — long trails smear on slow frames
        };
        GenericBolt.Spawn(_portal.transform.position + exitDir * 0.6f, exitDir, spec, TeamId, ownerRoot);

        // In-flash at the muzzle (orange) and out-flash at the portal (cyan).
        VfxUtil.ImpactBurst(muzzle.position + direction.normalized * 0.4f, new Color(1f, 0.6f, 0.2f));
        VfxUtil.ImpactBurst(_portal.transform.position, color);
        FlashMuzzle(3f);
    }

    void PlacePortal(Vector3 direction)
    {
        if (!Physics.Raycast(muzzle.position, direction, out RaycastHit hit, range,
                ~0, QueryTriggerInteraction.Ignore) || hit.transform.root == ownerRoot)
            return;
        _portal = PortalEntity.Spawn(hit.point, hit.normal, color);
        VfxUtil.Explosion(hit.point + hit.normal * 0.3f, color, 0.7f);
        FlashMuzzle(5f);
    }
}
