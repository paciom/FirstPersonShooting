using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One countermeasure flare: a bright, falling decoy that IS a target as far
/// as the missiles are concerned — it carries a one-point EnergyShield on the
/// owner's team and a real collider, so a seduced missile chases it exactly
/// the way it chases a jet, detonates against it, and the kill happens metres
/// behind the jet that dropped it. No missile ever special-cases "flare":
/// being a legitimate small shield is the whole trick.
///
/// Lives at the scene root like everything shootable. Popped in bursts of
/// three by <see cref="Pop"/> — one flare is a coin toss, a spread is a
/// pattern, and the pattern is what reads from the chase camera.
/// </summary>
public class DogfightFlare : MonoBehaviour
{
    /// <summary>Flares per burst, and the pattern's half-spread in degrees.</summary>
    const int BurstCount = 3;
    const float SpreadDegrees = 24f;

    const float EjectSpeed = 14f;
    const float LifeSeconds = 2.2f;
    const float Drag = 1.4f;

    static readonly Color Burn = new Color(1f, 0.78f, 0.35f);

    static readonly List<DogfightFlare> Live = new List<DogfightFlare>();

    public static IReadOnlyList<DogfightFlare> All => Live;

    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    public int Team { get; private set; }

    Vector3 _velocity;
    float _dieAt;
    Transform _quad;

    void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    void OnDisable()
    {
        Live.Remove(this);
    }

    /// <summary>The burst: three flares ejected down and back off the jet's
    /// tail, fanned so they separate on camera within the first half second.</summary>
    public static void Pop(JetPawn owner)
    {
        for (int i = 0; i < BurstCount; i++)
        {
            float fan = (i - (BurstCount - 1) * 0.5f) * SpreadDegrees;
            Vector3 eject = Quaternion.AngleAxis(fan, owner.transform.forward)
                            * (-owner.transform.up * 0.7f - owner.transform.forward * 0.7f);
            Spawn(owner.Center - owner.transform.forward * 1.2f,
                owner.Velocity * 0.35f + eject.normalized * EjectSpeed, owner.Team);
        }
        VfxUtil.SpawnBurst(owner.Center - owner.transform.forward * 1.2f, Burn, 10, 4f, 0.1f);
    }

    static void Spawn(Vector3 position, Vector3 velocity, int teamId)
    {
        // Built inactive, the TankPawn ritual: EnergyShield latches Current
        // from maxShield in Awake, and a flare that woke with the default
        // hundred points would be a decoy missiles bounce off rather than die on.
        var go = new GameObject("DogfightFlare");
        go.SetActive(false);
        go.transform.position = position;

        // The collider is what the missile's raycast detonates against; small,
        // non-trigger, Default layer — the bolt rules.
        var ball = go.AddComponent<SphereCollider>();
        ball.radius = 0.35f;

        var shield = go.AddComponent<EnergyShield>();
        shield.maxShield = 1f;
        shield.teamId = teamId;
        shield.regenPerSecond = 0f;

        var flare = go.AddComponent<DogfightFlare>();
        flare.Team = teamId;
        flare._velocity = velocity;
        flare._dieAt = Time.time + LifeSeconds;

        // The burn: a glow quad the camera is always facing, a warm light, and
        // a short trail so the fall reads as an arc rather than a popping dot.
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(quad.GetComponent<Collider>());
        quad.name = "Burn";
        quad.transform.SetParent(go.transform, false);
        quad.transform.localScale = Vector3.one * 1.1f;
        quad.GetComponent<MeshRenderer>().sharedMaterial =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, Burn, 2.2f);
        flare._quad = quad.transform;

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Burn;
        light.intensity = 2.4f;
        light.range = 6f;

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.4f;
        trail.startWidth = 0.16f;
        trail.endWidth = 0f;
        trail.material = VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, Burn, 1.8f);

        go.SetActive(true);
        shield.OnDeRezzed += flare.Struck;
    }

    void Struck()
    {
        VfxUtil.SpawnBurst(transform.position, Burn, 6, 3f, 0.09f);
        Destroy(gameObject);
    }

    void Update()
    {
        if (Time.time >= _dieAt)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.deltaTime;
        _velocity += Vector3.down * (9f * dt);
        _velocity *= Mathf.Max(0f, 1f - Drag * dt);
        transform.position += _velocity * dt;

        // The burn faces whoever is watching, and gutters as it dies.
        var camera = Camera.main;
        if (camera != null && _quad != null)
            _quad.rotation = Quaternion.LookRotation(
                transform.position - camera.transform.position);
        float left = Mathf.Clamp01((_dieAt - Time.time) / LifeSeconds);
        if (_quad != null)
            _quad.localScale = Vector3.one * (0.5f + 0.6f * left + 0.1f * Mathf.Sin(Time.time * 31f));
    }

    void OnDestroy()
    {
        var shield = GetComponent<EnergyShield>();
        if (shield != null)
            shield.OnDeRezzed -= Struck;
    }
}
