using UnityEngine;

/// <summary>
/// Ice construction: aim at the ground and a jagged wall of glowing crystal
/// erupts across the line — instant temporary cover that melts after a while.
/// </summary>
public class GlacierWall : Weapon
{
    public float wallsPerSecond = 0.15f;
    public int blocks = 5;
    float _nextFireTime;
    GameObject _activeWall;   // one wall per shooter — six bots casting freely builds a maze

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Glacier Wall";
        color = new Color(0.55f, 0.85f, 1f);
        preferredRange = 14f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        if (_activeWall != null)
            return;   // wait for the current wall to melt

        // Find the ground point the player is aiming at (or drop from max range).
        Vector3 dir = direction.normalized;
        Vector3 point;
        if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, 22f, ~0, QueryTriggerInteraction.Ignore)
            && hit.transform.root != ownerRoot)
            point = hit.point;
        else
            point = muzzle.position + dir * 10f;

        // Snap to the actual floor: ignore ice blocks and characters, take the
        // lowest solid surface — otherwise walls stack on top of old walls and
        // climb into the sky.
        float lowestY = float.MaxValue;
        foreach (var below in Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 40f,
                     ~0, QueryTriggerInteraction.Ignore))
        {
            if (below.collider.GetComponentInParent<IceWallEntity>() != null)
                continue;
            if (below.transform.root.GetComponent<EnergyShield>() != null)
                continue;
            if (below.point.y < lowestY)
            {
                lowestY = below.point.y;
                point = below.point;
            }
        }

        _nextFireTime = Time.time + 1f / wallsPerSecond;

        // Wall runs perpendicular to the aim direction.
        Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
        _activeWall = IceWallEntity.SpawnWall(point, right, blocks, color);
        FlashMuzzle(4f);
    }
}
