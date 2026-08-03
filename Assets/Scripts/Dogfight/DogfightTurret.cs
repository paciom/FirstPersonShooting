using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A PHOTON TURRET: a ground battery under the dogfight that locks onto
/// whichever jet strays into its dome and puts a slow, heavy missile up after
/// it. Neutral — team 2 — so it is everyone's problem, which is half of why
/// it exists: two symmetric pilots fed the same threats fly the same fight,
/// and a battery that fires on whoever is lowest keeps shuffling the deck.
/// The other half is the player's: something on the ground worth diving at.
///
/// EVERYTHING IS TELEGRAPHED. The head turns to face you first, the lamp
/// charges from ember to furnace over the whole lock, and only then does the
/// rail fire — a kid who gets hit knew it was coming, and a kid who breaks
/// and flares beat it honestly. The rail's missile is a real Missiles Pack
/// body that visibly reloads: an armed turret and an empty one read
/// differently from altitude.
///
/// Lives at the SCENE ROOT with a real shield and colliders — turrets are
/// shootable by the same rule as everything else in the project, and killing
/// the battery is the standing invitation to fly low.
/// </summary>
public class DogfightTurret : MonoBehaviour
{
    /// <summary>The neutral team. Never fed to MatchAnnouncer.TeamColor —
    /// that table knows two teams; the battery's colour lives here.</summary>
    public const int Team = 2;

    static readonly Color Paint = new Color(1f, 0.55f, 0.1f);   // the arena's third accent

    const int RingCount = 4;
    const float Shield = 70f;

    /// <summary>The engagement dome, and the floor under it: a turret ignores
    /// anything below the soft floor's approach so the intro on the pads is
    /// never interrupted by artillery.</summary>
    const float Range = 130f;
    const float MinTargetAltitude = 10f;

    const float LockSeconds = 1.4f;
    const float ReloadSeconds = 7.5f;
    const float SlewDegreesPerSecond = 130f;

    static readonly List<DogfightTurret> Live = new List<DogfightTurret>();

    public static IReadOnlyList<DogfightTurret> All => Live;

    /// <summary>False until the mode declares the fight open — the battery
    /// tracks the climb-out but holds its fire. ASSERTED BY THE MODE EVERY
    /// FRAME (statics die in a mid-Play recompile), never set-and-forgotten.</summary>
    public static bool WeaponsFree;

    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    /// <summary>Nearest live turret, for the player's missile lock to offer.</summary>
    public static DogfightTurret Nearest(Vector3 from, float maxRange = float.MaxValue)
    {
        DogfightTurret best = null;
        float bestSqr = maxRange * maxRange;
        foreach (var turret in Live)
        {
            if (turret == null || turret.IsDead)
                continue;
            float sqr = (turret.transform.position - from).sqrMagnitude;
            if (sqr > bestSqr)
                continue;
            bestSqr = sqr;
            best = turret;
        }
        return best;
    }

    public bool IsDead { get; private set; }

    /// <summary>Where a lock diamond or a fuse should aim: the head, not the feet.</summary>
    public Vector3 Center => transform.position + Vector3.up * 5f;

    EnergyShield _shield;
    Transform _yawPivot;
    Transform _pitchPivot;
    Transform _railMissile;
    MeshRenderer _lamp;
    Material _lampGlow;

    JetPawn _target;
    float _lockProgress;
    float _reloadUntil;

    /// <summary>
    /// The battery: a fixed, seeded ring on the deck, azimuths staggered and
    /// every reload clock started out of phase — four turrets that fired as
    /// one would read as a scripted volley, and dodge as one problem.
    /// </summary>
    public static void BuildRing()
    {
        var random = new System.Random(53);
        for (int i = 0; i < RingCount; i++)
        {
            float azimuth = (i + 0.5f) * (360f / RingCount)
                            + (float)random.NextDouble() * 30f;
            float distance = Mathf.Lerp(DogfightSky.Radius * 0.3f, DogfightSky.Radius * 0.7f,
                (float)random.NextDouble());
            Vector3 at = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward * distance;
            Spawn(at, (float)random.NextDouble() * ReloadSeconds);
        }
    }

    static void Spawn(Vector3 position, float reloadPhase)
    {
        // Inactive while described — the shield ritual, as everywhere.
        var go = new GameObject("DogfightTurret");
        go.SetActive(false);
        go.transform.position = position;

        var turret = go.AddComponent<DogfightTurret>();
        turret._reloadUntil = Time.time + reloadPhase;
        turret.BuildBody(go.transform);

        turret._shield = go.AddComponent<EnergyShield>();
        turret._shield.maxShield = Shield;
        turret._shield.teamId = Team;
        turret._shield.regenPerSecond = 0f;

        go.SetActive(true);
        turret._shield.OnDeRezzed += turret.Collapse;
    }

    void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    void OnDisable()
    {
        Live.Remove(this);
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= Collapse;
    }

    /// <summary>Base, column, turning head, twin rails, one visible missile,
    /// one warning lamp. Primitives in the arena's own materials — the same
    /// language the sky's spires and pads speak.</summary>
    void BuildBody(Transform root)
    {
        var armor = ArenaMaterials.Lit("dogfight-turret", new Color(0.11f, 0.15f, 0.23f), 0.45f);
        var trim = ArenaMaterials.Emissive("dogfight-turret-trim", Paint, 1.8f);

        var footing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        footing.name = "Base";
        footing.transform.SetParent(root, false);
        footing.transform.localScale = new Vector3(5.2f, 0.8f, 5.2f);
        footing.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        footing.GetComponent<MeshRenderer>().sharedMaterial = armor;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(ring.GetComponent<Collider>());
        ring.name = "BaseRing";
        ring.transform.SetParent(footing.transform, false);
        ring.transform.localScale = new Vector3(1.03f, 0.06f, 1.03f);
        ring.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = trim;

        var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        column.name = "Column";
        column.transform.SetParent(root, false);
        column.transform.localScale = new Vector3(1.6f, 1.5f, 1.6f);
        column.transform.localPosition = new Vector3(0f, 3f, 0f);
        column.GetComponent<MeshRenderer>().sharedMaterial = armor;

        _yawPivot = new GameObject("YawPivot").transform;
        _yawPivot.SetParent(root, false);
        _yawPivot.localPosition = new Vector3(0f, 4.8f, 0f);

        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(_yawPivot, false);
        head.transform.localScale = new Vector3(2.4f, 1.3f, 2.8f);
        head.GetComponent<MeshRenderer>().sharedMaterial = armor;

        _pitchPivot = new GameObject("PitchPivot").transform;
        _pitchPivot.SetParent(_yawPivot, false);
        _pitchPivot.localPosition = new Vector3(0f, 0.55f, 0f);

        for (int side = 0; side < 2; side++)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(rail.GetComponent<Collider>());
            rail.name = "Rail";
            rail.transform.SetParent(_pitchPivot, false);
            rail.transform.localScale = new Vector3(0.22f, 0.22f, 2.6f);
            rail.transform.localPosition = new Vector3(side == 0 ? -0.7f : 0.7f, 0.35f, 0.2f);
            rail.GetComponent<MeshRenderer>().sharedMaterial = armor;
        }

        // The round on the rail. Hidden while reloading — see the class note.
        // The holder is what toggles, so a project missing the pack still
        // fires (invisibly armed beats never armed).
        var mount = new GameObject("RailRound").transform;
        mount.SetParent(_pitchPivot, false);
        mount.localPosition = new Vector3(0f, 0.35f, 0.3f);
        MissileModels.Attach(mount, 2, 1.5f);
        _railMissile = mount;

        var lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(lampGo.GetComponent<Collider>());
        lampGo.name = "Lamp";
        lampGo.transform.SetParent(_yawPivot, false);
        lampGo.transform.localScale = Vector3.one * 0.5f;
        lampGo.transform.localPosition = new Vector3(0f, 0.95f, -1.1f);
        _lamp = lampGo.GetComponent<MeshRenderer>();
        // A solid glowing sphere; intensity rides _BaseColor and never crosses
        // the 2.5 bloom-whiteout line even at full furnace.
        _lampGlow = VfxUtil.MakeGlowMaterial(Paint, 0.3f);
        _lamp.material = _lampGlow;
    }

    void Update()
    {
        if (IsDead)
            return;

        float dt = Time.deltaTime;
        Acquire();

        bool armed = Time.time >= _reloadUntil;
        if (_railMissile != null)
            _railMissile.gameObject.SetActive(armed);

        if (_target == null)
        {
            _lockProgress = Mathf.Max(0f, _lockProgress - dt * 2f);
            SetLamp(0.25f);
            return;
        }

        Track(dt);

        if (!armed || !WeaponsFree)
        {
            _lockProgress = 0f;
            SetLamp(0.25f);
            return;
        }

        _lockProgress += dt;
        // Ember to furnace across the lock — THE telegraph.
        SetLamp(Mathf.Lerp(0.3f, 2.4f, Mathf.Clamp01(_lockProgress / LockSeconds)));

        if (_lockProgress >= LockSeconds)
            Fire();
    }

    /// <summary>Nearest jet inside the dome and above the pad layer. Re-run
    /// every frame; a lock in progress survives a re-pick of the same jet and
    /// dies with a target that left the dome.</summary>
    void Acquire()
    {
        JetPawn best = null;
        float bestSqr = Range * Range;
        foreach (var jet in JetPawn.All)
        {
            if (jet == null || jet.IsDown || !jet.FlightOn
                || jet.transform.position.y < MinTargetAltitude)
                continue;
            float sqr = (jet.transform.position - transform.position).sqrMagnitude;
            if (sqr > bestSqr)
                continue;
            bestSqr = sqr;
            best = jet;
        }
        if (best != _target)
        {
            _target = best;
            _lockProgress = 0f;
        }
    }

    void Track(float dt)
    {
        Vector3 to = _target.Center - _yawPivot.position;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.sqrMagnitude > 1e-4f)
            _yawPivot.rotation = Quaternion.RotateTowards(_yawPivot.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                SlewDegreesPerSecond * dt);

        float pitch = Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
        _pitchPivot.localRotation = Quaternion.RotateTowards(_pitchPivot.localRotation,
            Quaternion.Euler(-pitch, 0f, 0f), SlewDegreesPerSecond * dt);
    }

    void Fire()
    {
        _lockProgress = 0f;
        _reloadUntil = Time.time + ReloadSeconds;

        Vector3 from = _pitchPivot.TransformPoint(new Vector3(0f, 0.35f, 1.8f));
        Vector3 aim = (_target.Center - from).normalized;
        DogfightMissile.Launch(from, aim, Team, transform, _target.transform,
            DogfightMissile.Flavor.Turret);
        VfxUtil.SpawnBurst(from, Paint, 10, 4f, 0.12f);
    }

    void SetLamp(float intensity)
    {
        if (_lampGlow != null)
            _lampGlow.SetColor("_BaseColor", Paint * intensity);
    }

    /// <summary>Shot to death: one blast, the head slumps dark, and the base
    /// stays as a trophy. No respawn — a cleared corridor STAYS cleared,
    /// which is what makes clearing it worth the dive.</summary>
    void Collapse()
    {
        if (IsDead)
            return;
        IsDead = true;
        VfxUtil.Explosion(Center, Paint, 1.6f);
        // The battery goes up properly — War FX blast, then a burn that
        // marks the trophy until teardown sweeps the turret away.
        WarFx.Spawn(WarFx.Kind.Big, Center, 1.4f);
        WarFx.AttachFire(_yawPivot, Vector3.up * 0.5f, 1.3f);
        if (_railMissile != null)
            _railMissile.gameObject.SetActive(false);
        if (_pitchPivot != null)
            _pitchPivot.localRotation = Quaternion.Euler(24f, 0f, 6f);
        if (_yawPivot != null)
            _yawPivot.localRotation *= Quaternion.Euler(0f, 0f, 7f);
        SetLamp(0.05f);
    }
}
