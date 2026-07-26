using UnityEngine;

/// <summary>
/// Weather: tags an enemy with their very own personal storm cloud, which
/// follows them around raining and zapping for several seconds.
/// </summary>
public class ThundercloudPet : Weapon
{
    public float cooldownSeconds = 5f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Thundercloud Pet";
        color = new Color(0.55f, 0.65f, 1f);
        if (damage == 20f) damage = 7f;   // per zap
        preferredRange = 22f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;

        // Needs a direct hit to attach the cloud.
        if (!Physics.Raycast(muzzle.position, direction.normalized, out RaycastHit hit, range,
                ~0, QueryTriggerInteraction.Ignore) || hit.transform.root == ownerRoot)
            return;

        var shield = hit.transform.root.GetComponent<EnergyShield>();
        if (shield == null || shield.teamId == TeamId)
        {
            // Missed: small spark so the attempt still reads on camera.
            VfxUtil.ImpactBurst(hit.point, color);
            _nextFireTime = Time.time + 0.4f;
            return;
        }

        _nextFireTime = Time.time + cooldownSeconds;
        ThundercloudEntity.Spawn(shield, ownerRoot, color);
        FadingLine.Spawn(muzzle.position, hit.point, color, 0.07f, 0.2f, 3.5f);
        FlashMuzzle(4f);
    }
}
