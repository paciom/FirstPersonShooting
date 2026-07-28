using UnityEngine;

/// <summary>
/// Micro singularity: throws a tiny black hole that anchors mid-air, drags
/// enemies toward its swirling accretion ring, then snaps back to your hand.
/// </summary>
public class BlackHoleYoyo : Weapon
{
    public float cooldownSeconds = 5f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Black Hole Yo-yo";
        color = new Color(0.7f, 0.4f, 1f);
        preferredRange = 16f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + cooldownSeconds;

        BlackHoleEntity.Spawn(muzzle.position, direction.normalized, TeamId, ownerRoot);
        FlashMuzzle(5f);
    }
}
