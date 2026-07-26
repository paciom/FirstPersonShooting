using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centripetal force: builds up to three glowing moons that orbit you as a
/// bodyguard shield — fire again with all three up and they slingshot at
/// whatever you're aiming at.
/// </summary>
public class OrbitLauncher : Weapon
{
    public float spawnsPerSecond = 1.2f;
    public int maxOrbs = 3;
    float _nextActionTime;
    readonly List<OrbitOrbEntity> _orbs = new List<OrbitOrbEntity>();

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Orbit Launcher";
        color = new Color(0.8f, 0.45f, 1f);   // saturated violet — pastel read as white
        if (damage == 20f) damage = 16f;
        preferredRange = 12f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextActionTime)
            return;
        _nextActionTime = Time.time + 1f / spawnsPerSecond;

        _orbs.RemoveAll(o => o == null);

        if (_orbs.Count < maxOrbs)
        {
            // Add a moon to the constellation.
            var orbGo = WeaponUtil.GlowPrimitive(PrimitiveType.Sphere,
                ownerRoot.position + Vector3.up * 1.3f, Vector3.one * 0.32f, color, 1.7f);
            orbGo.name = "OrbitOrb";
            var trail = orbGo.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0f;
            trail.material = VfxUtil.MakeGlowMaterial(color, 1.4f);

            var orb = orbGo.AddComponent<OrbitOrbEntity>();
            orb.ownerRoot = ownerRoot;
            orb.teamId = TeamId;
            orb.color = color;
            orb.damage = damage * 0.6f;
            orb.angle = _orbs.Count * (360f / maxOrbs);
            _orbs.Add(orb);
            FlashMuzzle(3f);
        }
        else
        {
            // Full constellation: slingshot them all downrange.
            StartCoroutine(FlingAll(direction.normalized));
        }
    }

    IEnumerator FlingAll(Vector3 direction)
    {
        foreach (var orb in new List<OrbitOrbEntity>(_orbs))
        {
            if (orb != null)
            {
                orb.Fling(direction, damage);
                FlashMuzzle(5f);
            }
            yield return new WaitForSeconds(0.12f);
        }
        _orbs.Clear();
    }
}
