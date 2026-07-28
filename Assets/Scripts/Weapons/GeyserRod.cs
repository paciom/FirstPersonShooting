using UnityEngine;

/// <summary>
/// Hydro pressure: strike the ground under a target and a white-blue water
/// column erupts beneath them, launching them skyward in a cloud of mist.
/// </summary>
public class GeyserRod : Weapon
{
    public float shotsPerSecond = 0.7f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Geyser Rod";
        color = new Color(0.5f, 0.85f, 1f);
        if (damage == 20f) damage = 22f;
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

        // The geyser erupts from the floor under the aim point.
        if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 40f,
                ~0, QueryTriggerInteraction.Ignore))
            point = ground.point;

        GeyserEntity.Spawn(point, damage, TeamId, ownerRoot);
        FadingLine.Spawn(muzzle.position, point, color, 0.06f, 0.2f, 3f);
        FlashMuzzle(3.5f);
    }
}
