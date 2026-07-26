using UnityEngine;

/// <summary>
/// Rhythmic pressure: fires quantized to a steady beat. Every fourth beat is
/// the accent — a bigger, harder-hitting shot with a cymbal-crash burst.
/// </summary>
public class DrumlineCannon : Weapon
{
    public float beatsPerMinute = 132f;

    float _nextBeatTime;
    int _beatIndex;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Drumline Cannon";
        color = new Color(1f, 0.8f, 0.4f);
        if (damage == 20f) damage = 14f;
        preferredRange = 18f;
    }

    public override void TryFire(Vector3 direction)
    {
        // Shots only leave on the beat — hold the trigger and it plays the rhythm.
        if (Time.time < _nextBeatTime)
            return;
        _nextBeatTime = Time.time + 60f / beatsPerMinute;
        _beatIndex++;

        bool accent = _beatIndex % 4 == 0;
        var spec = new BoltSpec
        {
            speed = 36f,
            damage = accent ? damage * 2f : damage,
            color = accent ? Color.white : color,
            glow = accent ? 4.5f : 2.4f,   // accents flash white; regular beats stay gold
            size = accent ? 0.2f : 0.1f,
            trailTime = accent ? 0.3f : 0.15f,
            splashRadius = accent ? 2f : 0f,
            splashScale = 0.8f,
        };
        GenericBolt.Spawn(muzzle.position, direction.normalized, spec, TeamId, ownerRoot);
        FlashMuzzle(accent ? 6f : 3f);   // beat-synced muzzle rings
    }
}
