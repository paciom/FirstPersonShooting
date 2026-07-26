using UnityEngine;

/// <summary>
/// Anti-gravity: a lavender ray that switches off its victim's gravity — they
/// drift up helplessly in a cloud of zero-g sparkles for a few seconds.
/// </summary>
public class MoonbootsBeam : Weapon
{
    public float shotsPerSecond = 1f;
    public float floatSeconds = 3f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Moonboots Beam";
        color = new Color(0.8f, 0.65f, 1f);
        if (damage == 20f) damage = 8f;
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;
        Vector3 end = origin + dir * range;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore)
            && hit.transform.root != ownerRoot)
        {
            end = hit.point;
            var shield = hit.transform.root.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId != TeamId)
            {
                shield.TakeHit(damage, hit.point, ownerRoot);
                StatusEffects.Get(shield.transform.root)?.ApplyFloat(floatSeconds);
                // Zero-g dust drifting up around them.
                VfxUtil.SpawnBurst(WeaponUtil.Center(shield), color, 12, 1.2f);
            }
        }

        // Two-layer ray: thin bright core in a wide lavender halo — reads
        // anti-grav purple instead of a flat white line.
        FadingLine.Spawn(origin, end, Color.white, 0.03f, 0.3f, 2.4f);
        FadingLine.Spawn(origin, end, color, 0.14f, 0.35f, 1.2f);
        FlashMuzzle(3.5f);
    }
}
