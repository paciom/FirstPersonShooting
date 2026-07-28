using UnityEngine;

/// <summary>
/// The vehicle's second gun: a lobbed splash shell.
///
/// The cannon is flat, fast and single-target, so on its own it made tank form
/// one long-range poke and nothing else. The mortar arcs, which gives driving a
/// second thing to be good at — it lands behind cover and it punishes anything
/// standing still, so a robot that ducks out of the cannon's line is not
/// automatically safe.
///
/// Together they are the reason to be a tank at range: a direct shell for a
/// clean shot and an arc for a target that will not offer one. Both are slow,
/// so neither closes the gap with a robot's sustained fire up close.
/// </summary>
public class VehicleMortar : Weapon
{
    [Header("Vehicle Mortar")]
    [Tooltip("Shots per second. Slower even than the cannon — this is the " +
             "weapon you commit to a prediction with.")]
    public float shotsPerSecond = 0.5f;

    [Tooltip("Seconds the shell spends in the air. Longer arcs higher, clears " +
             "taller cover, and gives the target more time to leave.")]
    public float flightTime = 1.1f;

    public float splashRadius = 4.5f;

    [Tooltip("Cap on launch speed. A shell that would need more than this " +
             "falls short rather than flattening into a fast line drive.")]
    public float maxLaunchSpeed = 45f;

    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Siege Mortar";
        damage = 38f;
        range = 55f;
        // Sits inside the cannon's preferred range, so a bot holding at
        // distance has one of the two well suited at any point in the approach.
        preferredRange = 24f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / Mathf.Max(0.05f, shotsPerSecond);

        // Aim at where the flat direction reaches over the flight time, then
        // solve the arc that lands there — same ballistics as PlasmaLobber, so
        // the two read as the same physics.
        Vector3 origin = muzzle.position;
        Vector3 aim = origin + direction.normalized * Mathf.Min(range, preferredRange * 1.4f);

        Vector3 delta = aim - origin;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float g = -PlasmaOrb.Gravity;
        Vector3 velocity = flat / flightTime
            + Vector3.up * (delta.y / flightTime + 0.5f * g * flightTime);
        if (velocity.magnitude > maxLaunchSpeed)
            velocity = velocity.normalized * maxLaunchSpeed;

        PlasmaOrb.Spawn(origin, velocity, damage, splashRadius, color, TeamId, ownerRoot);
        FlashMuzzle(6f);
    }
}
