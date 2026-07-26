using UnityEngine;

/// <summary>
/// Rebirth flame: a little firebird — golden body, flapping flame wings, and a
/// stream of red-orange embers — that, if it de-rezzes its victim, bursts into
/// golden light and is reborn flying at the next nearest enemy.
/// </summary>
public class PhoenixDart : Weapon
{
    public float shotsPerSecond = 2.2f;
    float _nextFireTime;

    static readonly Color EmberRed = new Color(1f, 0.35f, 0.1f);

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Phoenix Dart";
        color = new Color(1f, 0.7f, 0.2f);
        if (damage == 20f) damage = 22f;
        preferredRange = 20f;
    }

    public override void TryFire(Vector3 direction)
    {
        if (Time.time < _nextFireTime)
            return;
        _nextFireTime = Time.time + 1f / shotsPerSecond;

        LaunchBird(muzzle.position, direction.normalized);
        FlashMuzzle(3.5f);
    }

    void LaunchBird(Vector3 from, Vector3 dir)
    {
        var spec = new BoltSpec
        {
            speed = 34f,
            damage = damage,
            color = color,
            size = 0.12f,
            glow = 2.6f,        // golden body
            trailTime = 0.12f,  // short ribbon; the embers are the tail
            trailWidth = 0.1f,
            trailGlow = 1.4f,
            onShieldHit = Rebirth,
        };
        var bolt = GenericBolt.Spawn(from, dir, spec, TeamId, ownerRoot);
        DressAsPhoenix(bolt);
    }

    /// <summary>Wings + ember tail turn the plain dart into a firebird.</summary>
    void DressAsPhoenix(GenericBolt bolt)
    {
        // Wing rig: its entity keeps it aligned with flight and flaps.
        var rig = new GameObject("PhoenixWings");
        rig.transform.position = bolt.transform.position;
        rig.transform.SetParent(bolt.transform, true);

        var wings = rig.AddComponent<PhoenixWingsEntity>();
        wings.bolt = bolt;
        wings.leftWing = MakeWing(rig.transform, -1f);
        wings.rightWing = MakeWing(rig.transform, 1f);

        // Red-orange embers streaming behind — the burning tail feathers.
        var psGo = new GameObject("TailEmbers");
        psGo.transform.SetParent(bolt.transform, false);
        var ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = EmberRed * 1.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.gravityModifier = 0.15f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 32f;
        psGo.GetComponent<ParticleSystemRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.SparkTexture, Color.white, 1.4f);
        ps.Play();
    }

    Transform MakeWing(Transform rig, float side)
    {
        var wing = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(wing.GetComponent<Collider>());
        wing.name = side < 0 ? "WingL" : "WingR";
        wing.transform.SetParent(rig, false);
        wing.transform.localPosition = new Vector3(side * 0.28f, 0f, -0.1f);
        wing.transform.localScale = new Vector3(0.7f, 0.3f, 1f);
        // The streaky spark sprite reads as feathered flame.
        wing.GetComponent<MeshRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.SparkTexture, new Color(1f, 0.5f, 0.15f), 1.8f);
        return wing.transform;
    }

    void Rebirth(GenericBolt bolt, EnergyShield victim)
    {
        if (!victim.IsDown)
            return;   // only a de-rez triggers the phoenix

        var next = WeaponUtil.NearestEnemy(bolt.transform.position, TeamId, 40f, victim.transform.root);
        if (next == null)
            return;

        // Golden wing flare, then the bird is reborn toward its next victim.
        VfxUtil.Explosion(bolt.transform.position, color, 0.9f);
        Vector3 dir = (WeaponUtil.Center(next) - bolt.transform.position).normalized;
        LaunchBird(bolt.transform.position + dir * 0.5f, dir);
    }
}
