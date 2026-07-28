using UnityEngine;

/// <summary>
/// The gun a robot has while it is a vehicle: one heavy shell, slowly.
///
/// Vehicle form used to collapse to the robot's own first basic, which made
/// transforming a pure loss — the same gun, fewer of them. This is the other
/// half of that trade. Damage per shot is high enough to take roughly half a
/// shield in one hit, and the reload is long enough that missing genuinely
/// costs something, so driving is about picking a moment rather than holding a
/// trigger.
///
/// Sustained damage is deliberately well BELOW the robot's arsenal. A vehicle
/// that also out-damaged a robot would leave no reason to ever stand up.
///
/// Pairs with the rear weak spot in <see cref="EnergyShield"/>: a cannon shell
/// into an engine deck is the single hardest hit in the game, which is the
/// reward for flanking something that is armoured from the front.
/// </summary>
public class VehicleCannon : Weapon
{
    [Header("Vehicle Cannon")]
    [Tooltip("Shots per second. Low on purpose — this is the reload that pays " +
             "for the damage.")]
    public float shotsPerSecond = 0.7f;

    public float shellSpeed = 50f;

    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Siege Cannon";
        // Half a 100-point shield per shell, so two connect and a robot is down
        // — and a rear hit at 1.8x is very nearly a one-shot.
        damage = 45f;
        range = 70f;
        // Vehicles are fast and lightly armed up close; the AI should use one
        // to fight at a distance, not to brawl.
        preferredRange = 30f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;

        _nextFireTime = Time.time + 1f / Mathf.Max(0.05f, shotsPerSecond);
        LaserBolt.Spawn(muzzle.position, direction.normalized, shellSpeed, damage,
                        color, TeamId, ownerRoot);
        FlashMuzzle(7f);
    }
}
