using UnityEngine;

/// <summary>
/// Subsonic: charge it up, then drop the bass — a visible pressure ring blasts
/// outward and everyone nearby is knocked into the air.
/// </summary>
public class BassDropper : Weapon
{
    public float chargeTime = 0.9f;
    public float blastRadius = 8f;
    public float cooldownAfterShot = 0.8f;

    float _charge;
    float _readyTime;
    bool _firedThisFrame;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Bass Dropper";
        color = new Color(0.9f, 0.35f, 1f);
        if (damage == 20f) damage = 30f;
        preferredRange = 5f;
        range = blastRadius;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        if (Time.time < _readyTime)
            return;

        _charge += Time.deltaTime;
        FlashMuzzle(1f + 4f * (_charge / chargeTime));

        if (_charge < chargeTime)
            return;
        _charge = 0f;
        _readyTime = Time.time + cooldownAfterShot;

        Vector3 center = ownerRoot.position + Vector3.up * 1f;
        VfxUtil.Explosion(center, color, 2.2f);   // concentric pressure rings

        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            float d = Vector3.Distance(shield.transform.position, center);
            if (d > blastRadius)
                continue;
            float falloff = 1f - d / blastRadius;
            shield.TakeHit(damage * falloff, WeaponUtil.Center(shield), ownerRoot);

            var fx = StatusEffects.Get(shield.transform.root);
            if (fx != null)
            {
                Vector3 away = (shield.transform.position - center).normalized;
                fx.AddImpulse(away * (7f * falloff) + Vector3.up * (8f * falloff));
                fx.ApplyFloat(0.35f);   // that satisfying pop into the air
            }
        }
        FlashMuzzle(7f);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame && _charge > 0f)
            _charge = Mathf.Max(0f, _charge - Time.deltaTime * 1.5f);
        _firedThisFrame = false;
    }
}
