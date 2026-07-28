using UnityEngine;

/// <summary>
/// Rapid-growth flora: the shot makes neon vines whip out of the ground and
/// grab the nearest enemy — rooted in place among drifting firefly sparks.
/// </summary>
public class VineSnare : Weapon
{
    public float shotsPerSecond = 0.8f;
    public float snareRadius = 3f;
    public float rootSeconds = 1.8f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Vine Snare";
        color = new Color(0.35f, 1f, 0.3f);
        if (damage == 20f) damage = 12f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        Vector3 point = muzzle.position + direction.normalized * range;
        if (Physics.Raycast(muzzle.position, direction.normalized, out RaycastHit hit, range,
                ~0, QueryTriggerInteraction.Ignore) && hit.transform.root != ownerRoot)
            point = hit.point;

        // Vines grab anyone close to where the shot landed.
        bool caught = false;
        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            if (Vector3.Distance(shield.transform.position, point) > snareRadius)
                continue;
            caught = true;
            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            StatusEffects.Get(shield.transform.root)?.ApplyStuck(rootSeconds);

            // Vines whip up out of the ground around them.
            Vector3 feet = shield.transform.position;
            for (int i = 0; i < 4; i++)
            {
                Vector3 root = feet + new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f));
                Vector3 tip = feet + Vector3.up * Random.Range(1.2f, 2f)
                    + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
                FadingLine.SpawnJagged(root, tip, color, 0.06f, rootSeconds * 0.7f, 0.3f, 6, 1.8f);   // green vines
            }
            // Fireflies.
            VfxUtil.SpawnBurst(feet + Vector3.up * 1f, color, 10, 1f);
        }

        if (!caught)
            VfxUtil.ImpactBurst(point, color);   // vines rustle where the shot landed
        FlashMuzzle(3.5f);
    }
}
