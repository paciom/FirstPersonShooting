using System.Collections;
using UnityEngine;

/// <summary>
/// Ionized particles: fires a pillar of light skyward; a beat later, glittering
/// charged rain hammers down on the marked zone.
/// </summary>
public class IonRain : Weapon
{
    public float shotsPerSecond = 0.45f;
    public float zoneRadius = 3.5f;
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Ion Rain";
        color = new Color(0.45f, 0.7f, 1f);
        if (damage == 20f) damage = 18f;   // per second of rain
        preferredRange = 26f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        // Where does the strike land? Aim ray, capped at range.
        Vector3 target = muzzle.position + direction.normalized * range;
        if (Physics.Raycast(muzzle.position, direction.normalized, out RaycastHit hit, range,
                ~0, QueryTriggerInteraction.Ignore) && hit.transform.root != ownerRoot)
            target = hit.point;

        StartCoroutine(Strike(target));
        FlashMuzzle(4f);
    }

    IEnumerator Strike(Vector3 target)
    {
        // Pillar of light going up — the "calling the rain" tell.
        FadingLine.Spawn(target, target + Vector3.up * 30f, color, 0.25f, 0.5f, 4f);
        yield return new WaitForSeconds(0.6f);

        var zone = EffectZone.Spawn(target + Vector3.up * 0.5f, zoneRadius, 2.2f, color,
            ZoneParticles.Rain, TeamId, ownerRoot, showDome: false);
        zone.tickDamagePerSecond = damage;
        // Victims are temporarily IONIZED: slowed, aim scrambled, and visibly
        // crackling. The charge bleeds off ~1.4s after they escape the rain —
        // back to normal, minus whatever shield the rain cost them.
        zone.onCharacterTick = (z, shield) =>
        {
            var fx = StatusEffects.Get(shield.transform.root);
            if (fx != null)
            {
                fx.ApplySlow(0.6f, 1.4f);
                fx.ApplyBlind(1.4f);
            }
            if (Random.value < 0.35f)
            {
                Vector3 center = WeaponUtil.Center(shield);
                WeaponUtil.LightningArc(center + Vector3.up * 1.1f, center, z.color, 0.1f);
            }
        };
    }
}
