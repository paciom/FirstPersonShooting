using UnityEngine;

/// <summary>
/// Long-range charge sniper. Holding the trigger builds a charge (a growing
/// muzzle glow the camera can read); at full charge it fires an instant hitscan
/// rail — a bright neon line that snaps out and fades. Hits hard, but the
/// wind-up and single-shot cadence keep it fair and dramatic.
/// </summary>
public class RailZapper : Weapon
{
    [Header("Rail Zapper")]
    public float chargeTime = 1.3f;
    public float railRange = 90f;
    public float cooldownAfterShot = 0.4f;

    float _charge;
    float _readyTime;
    bool _firedThisFrame;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Rail Zapper";
        if (damage == 20f) damage = 70f;   // one big hit
        range = railRange;
        preferredRange = 42f;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        if (Time.time < _readyTime)
            return;

        _charge += Time.deltaTime;
        FlashMuzzle(1f + 4f * (_charge / chargeTime));   // glow swells with charge

        if (_charge >= chargeTime)
        {
            Fire(direction.normalized);
            _charge = 0f;
            _readyTime = Time.time + cooldownAfterShot;
        }
    }

    void Fire(Vector3 dir)
    {
        Vector3 origin = muzzle.position;
        Vector3 end = origin + dir * railRange;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, railRange, ~0, QueryTriggerInteraction.Ignore))
        {
            // Ignore the shooter's own colliders by nudging past them if needed.
            if (hit.transform.root != ownerRoot)
            {
                end = hit.point;
                ApplyHit(hit, damage);
            }
        }

        FadingLine.Spawn(origin, end, color, 0.12f, 0.35f, 5f);
        FlashMuzzle(6f);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        // Trigger released: bleed off the charge so you can't hold it forever.
        if (!_firedThisFrame && _charge > 0f)
            _charge = Mathf.Max(0f, _charge - Time.deltaTime * 1.5f);
        _firedThisFrame = false;
    }
}
