using UnityEngine;

/// <summary>
/// High frequency: a warping cone of sound that rattles enemies' aim — visible
/// zigzag lines fan out and dizzy sparkles orbit the victims.
/// </summary>
public class SonicScreech : Weapon
{
    public float pulsesPerSecond = 1.2f;
    public float coneRange = 12f;
    public float coneDot = 0.7f;   // ~45° cone
    float _nextFireTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Sonic Screech";
        color = new Color(0.4f, 1f, 0.8f);
        if (damage == 20f) damage = 12f;
        preferredRange = 9f;
        range = coneRange;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / pulsesPerSecond;

        Vector3 dir = direction.normalized;

        // The visible screech: three staggered sound-wave rings rippling
        // forward, plus a couple of short vibrato zigzags at the mouth.
        // (12m jagged lines just looked like cables dropped on the floor.)
        for (int i = 0; i < 3; i++)
            SonicWaveEntity.Spawn(muzzle.position + dir * 0.5f, dir, color, i * 0.09f);
        for (int i = 0; i < 2; i++)
        {
            Vector3 ray = Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(-14f, 14f), 0f) * dir;
            FadingLine.SpawnJagged(muzzle.position, muzzle.position + ray * 2.5f,
                color, 0.04f, 0.16f, 0.3f, 6, 1.7f);
        }

        foreach (var shield in WeaponUtil.FindEnemies(TeamId))
        {
            Vector3 to = WeaponUtil.Center(shield) - muzzle.position;
            if (to.magnitude > coneRange || Vector3.Dot(to.normalized, dir) < coneDot)
                continue;

            shield.TakeHit(damage, WeaponUtil.Center(shield), ownerRoot);
            var fx = StatusEffects.Get(shield.transform.root);
            fx?.ApplyBlind(1.4f);   // aim wobble
            // Dizzy-stars halo.
            VfxUtil.SpawnBurst(shield.transform.position + Vector3.up * 2.1f, Color.yellow, 6, 1.2f);
        }
        FlashMuzzle(4f);
    }
}
