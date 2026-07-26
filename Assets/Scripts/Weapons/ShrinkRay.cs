using UnityEngine;

/// <summary>
/// Mass compression: a purple spiral beam that squishes its victim to
/// half size with a cartoon *boing* — tiny, flustered, and briefly harmless-cute.
/// </summary>
public class ShrinkRay : Weapon
{
    public float shotsPerSecond = 0.8f;
    public float shrinkSeconds = 4f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Shrink Ray";
        color = new Color(0.75f, 0.3f, 1f);
        if (damage == 20f) damage = 10f;
        preferredRange = 18f;
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
                StatusEffects.Get(shield.transform.root)?.ApplyShrink(shrinkSeconds);
                // Tiny star pops around the newly-tiny victim.
                VfxUtil.SpawnBurst(WeaponUtil.Center(shield), Color.white, 8, 2f);
            }
        }

        // Spiral beam: three jittered strands reading as a corkscrew.
        for (int i = 0; i < 3; i++)
            FadingLine.SpawnJagged(origin, end, color, 0.04f, 0.3f, 0.22f, 10, 2.2f);   // stays purple
        FlashMuzzle(3.5f);
    }
}
