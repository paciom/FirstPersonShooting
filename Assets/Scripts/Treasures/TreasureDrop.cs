using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One treasure on the field. It gets there one of two ways:
///
///  * <see cref="Spawn"/> — the scheduled airdrop: a glowing crate under a
///    parachute, descending slowly onto a marked spot.
///  * <see cref="Launch"/> — kicked out of a cover block that just got shot to
///    pieces, tumbling along a ballistic arc onto nearby floor.
///
/// Either way it ends up as the same contested pickup. Everything is built from
/// primitives at runtime, matching the rest of the project (no prefabs, no
/// scene wiring). Two ways to interact:
///
///  * <b>Reach it</b> — proximity, not a trigger collider. Bots move on the
///    NavMesh and never physically collide, so OnTriggerEnter would only ever
///    fire for the player; checking distance against the handful of characters
///    is the one rule that works for everybody.
///  * <b>Shoot it</b> — a small non-trigger box on the crate makes it a
///    legitimate weapon target. Ordinary crates can be destroyed to deny them;
///    a Scrap Mine detonates on the first hit, which is the intended way to
///    deal with one.
/// </summary>
public class TreasureDrop : MonoBehaviour
{
    /// <summary>Every drop currently in the arena — AIBrain reads this to decide what to chase.</summary>
    public static readonly List<TreasureDrop> Active = new List<TreasureDrop>();

    const float FallSpeed = 3.2f;
    const float RestHeight = 0.55f;         // crate centre above the floor once landed
    const float PickupRadius = 1.7f;
    const float MineTriggerRadius = 1.9f;
    const float MineArmSeconds = 0.8f;      // grace after touchdown, so it can't clip someone instantly
    const float CrateHealth = 60f;
    const float GroundLifetime = 26f;
    const float SwaySettleHeight = 4f;      // sway damps out over the last few metres
    const float LaunchSeconds = 1.05f;      // block-pop arc time
    const float LaunchApex = 4.5f;          // metres above the higher end of the arc

    enum State { Falling, Launched, Landed, Gone }

    public TreasureDef Def { get; private set; }
    public bool IsHazard => Def != null && Def.hazard;
    public bool HasLanded => _state == State.Landed;
    public bool IsAvailable => _state != State.Gone;

    /// <summary>Where the crate is (or will be) standing — what bots path toward.</summary>
    public Vector3 GroundPoint => _ground;

    /// <summary>Where to shoot it — the crate itself, which is still in the air while falling.</summary>
    public Vector3 AimPoint => transform.position;

    State _state = State.Falling;
    Vector3 _ground;
    Transform _crate;
    Transform _chute;
    Light _beacon;
    GameObject _marker;
    Material _markerMaterial;
    Material _dangerMaterial;
    float _health = CrateHealth;
    float _phase;
    float _landedAt;
    float _nextReachScan;
    float _chuteCollapse;
    Vector3 _chuteScale;
    Vector3 _launchFrom;
    float _launchT;
    TrailRenderer _launchTrail;
    float _nextSupportScan;
    bool _armAnnounced;

    static readonly RaycastHit[] SupportProbe = new RaycastHit[12];

    /// <summary>
    /// Highest solid surface in the crate's column, searching down from
    /// <paramref name="fromY"/>. This is what stops a drop falling through
    /// scenery: the landing point is chosen from the world as it is right now,
    /// not from what was there when the drop was scheduled.
    ///
    /// Skips characters and other crates — a robot walking underneath is not
    /// something to land on, it is something to land next to.
    /// </summary>
    bool TryFindSupport(float fromY, float depth, out float surfaceY)
    {
        surfaceY = 0f;
        var origin = new Vector3(_ground.x, fromY + 0.25f, _ground.z);

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, SupportProbe,
                                            depth, ~0, QueryTriggerInteraction.Ignore);
        bool found = false;
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = SupportProbe[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(transform))
                continue;                                                   // ourselves
            if (hit.collider.GetComponentInParent<TreasureDrop>() != null)
                continue;                                                   // another crate
            if (hit.collider.GetComponentInParent<EnergyShield>() != null)
                continue;                                                   // a robot passing under
            if (hit.point.y > best)
            {
                best = hit.point.y;
                found = true;
            }
        }
        if (found)
            surfaceY = best;
        return found;
    }

    /// <summary>Move the planned landing surface, taking the ground marker with it.</summary>
    void SetGroundHeight(float surfaceY)
    {
        _ground.y = surfaceY;
        if (_marker != null)
            _marker.transform.position = _ground + Vector3.up * 0.03f;
    }

    // ------------------------------------------------------------------ spawn

    /// <summary>
    /// Scheduled airdrop: parachute <paramref name="def"/> onto
    /// <paramref name="groundPoint"/> from <paramref name="height"/> metres up.
    /// </summary>
    public static TreasureDrop Spawn(TreasureDef def, Vector3 groundPoint, float height)
    {
        var drop = Create(def, groundPoint, withChute: true);
        drop.transform.position = new Vector3(groundPoint.x, groundPoint.y + height, groundPoint.z);
        drop._state = State.Falling;
        return drop;
    }

    /// <summary>
    /// Kicked out of something that just broke: bursts out of
    /// <paramref name="from"/>, tumbles over an arc, and thumps down on
    /// <paramref name="groundPoint"/>. No parachute — this one is thrown.
    /// </summary>
    public static TreasureDrop Launch(TreasureDef def, Vector3 from, Vector3 groundPoint)
    {
        var drop = Create(def, groundPoint, withChute: false);
        drop.transform.position = from;
        drop._launchFrom = from;
        drop._state = State.Launched;

        // Sold as an eruption, not a spawn: a flash at the source, a shower of
        // sparks, and a bright trail the crate drags through the whole arc.
        VfxUtil.Explosion(from, def.color, 1.2f);
        VfxUtil.SpawnBurst(from, Color.white, 18, 7f);

        drop._launchTrail = drop._crate.gameObject.AddComponent<TrailRenderer>();
        drop._launchTrail.time = 0.35f;
        drop._launchTrail.startWidth = 0.45f;
        drop._launchTrail.endWidth = 0f;
        drop._launchTrail.material = VfxUtil.MakeGlowMaterial(def.color, 1.6f);
        return drop;
    }

    static TreasureDrop Create(TreasureDef def, Vector3 groundPoint, bool withChute)
    {
        var go = new GameObject($"TreasureDrop_{def.kind}");
        var drop = go.AddComponent<TreasureDrop>();
        drop.Def = def;
        drop._ground = groundPoint;
        drop._phase = Random.Range(0f, 10f);
        drop.Build(withChute);
        return drop;
    }

    void OnEnable() => Active.Add(this);

    void OnDisable() => Active.Remove(this);

    void OnDestroy()
    {
        if (_marker != null)
            Destroy(_marker);
    }

    // ------------------------------------------------------------------ build

    void Build(bool withChute)
    {
        _crate = new GameObject("Crate").transform;
        _crate.SetParent(transform, false);
        BuildPayload(_crate, Def);

        // Shootable hull. Non-trigger so weapon raycasts (which all ignore
        // triggers) can hit it; small enough that walking up to a crate reaches
        // pickup range well before bumping into the box.
        var hull = _crate.gameObject.AddComponent<BoxCollider>();
        hull.size = Vector3.one * 0.8f;

        var lightGo = new GameObject("Beacon");
        lightGo.transform.SetParent(_crate, false);
        _beacon = lightGo.AddComponent<Light>();
        _beacon.type = LightType.Point;
        _beacon.color = Def.color;
        // Modest range on purpose: a dozen crates can be live at once now, and
        // each one is a real-time point light.
        _beacon.range = 5.5f;
        _beacon.intensity = 2.2f;

        if (withChute)
            BuildChute();
        BuildGroundMarker();
    }

    /// <summary>Distinct silhouette per kind — the crate has to read at a glance from a spectator camera.</summary>
    static void BuildPayload(Transform parent, TreasureDef def)
    {
        switch (def.kind)
        {
            case TreasureKind.Bomb:
                // A spiked sphere: nothing else in the arena is shaped like this.
                Piece(parent, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.62f, def.color, 1.5f);
                Piece(parent, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.78f, 0.05f, 0.78f),
                    new Color(0.1f, 0.1f, 0.12f), 1f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f;
                    Piece(parent, PrimitiveType.Cube,
                        new Vector3(Mathf.Cos(a) * 0.36f, 0.18f, Mathf.Sin(a) * 0.36f),
                        new Vector3(0.1f, 0.22f, 0.1f), new Color(0.85f, 0.75f, 0.2f), 1.2f);
                }
                break;

            case TreasureKind.GoldBars:
                for (int i = 0; i < 3; i++)
                    Piece(parent, PrimitiveType.Cube, new Vector3(0f, -0.18f + i * 0.17f, 0f),
                        new Vector3(0.66f - i * 0.08f, 0.14f, 0.4f - i * 0.05f), def.color, 1.6f);
                break;

            case TreasureKind.RepairPack:
                Piece(parent, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.62f,
                    new Color(0.92f, 0.96f, 1f), 0.9f);
                Piece(parent, PrimitiveType.Cube, Vector3.forward * -0.33f, new Vector3(0.42f, 0.13f, 0.05f),
                    def.color, 1.8f);
                Piece(parent, PrimitiveType.Cube, Vector3.forward * -0.33f, new Vector3(0.13f, 0.42f, 0.05f),
                    def.color, 1.8f);
                break;

            case TreasureKind.WeaponPod:
                // Long pod, nose down — reads as "a gun came in this".
                Piece(parent, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.42f, 0.46f, 0.42f),
                    new Color(0.14f, 0.15f, 0.18f), 1f);
                Piece(parent, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.46f, 0.1f, 0.46f),
                    def.color, 1.7f);
                break;

            default:
                Piece(parent, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.6f,
                    new Color(0.13f, 0.14f, 0.17f), 1f);
                Piece(parent, PrimitiveType.Cube, Vector3.zero, Vector3.one * 0.34f, def.color, 1.7f);
                break;
        }
    }

    static GameObject Piece(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale,
        Color color, float glow)
    {
        var go = WeaponUtil.GlowPrimitive(type, Vector3.zero, scale, color, glow);
        go.name = type.ToString();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        return go;
    }

    void BuildChute()
    {
        _chute = new GameObject("Chute").transform;
        _chute.SetParent(transform, false);
        _chute.localPosition = Vector3.up * 1.75f;

        var canopy = WeaponUtil.GhostShell(PrimitiveType.Sphere, Vector3.zero,
            new Vector3(2.1f, 1.1f, 2.1f), Def.color, 0.5f);
        canopy.name = "Canopy";
        canopy.transform.SetParent(_chute, false);
        canopy.transform.localPosition = Vector3.zero;
        canopy.transform.localScale = new Vector3(2.1f, 1.1f, 2.1f);

        // Four risers down to the crate.
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            var cord = WeaponUtil.GlowPrimitive(PrimitiveType.Cube, Vector3.zero,
                Vector3.one, new Color(0.75f, 0.85f, 0.95f), 0.8f);
            cord.name = "Cord";
            cord.transform.SetParent(_chute, false);
            cord.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, -0.9f, Mathf.Sin(a) * 0.45f);
            cord.transform.localScale = new Vector3(0.03f, 1.8f, 0.03f);
            cord.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 14f, 0f, -Mathf.Cos(a) * 14f);
        }

        _chuteScale = _chute.localScale;
    }

    /// <summary>Landing pad on the floor: a ring under the drop, plus a blast ring for mines.</summary>
    void BuildGroundMarker()
    {
        _marker = new GameObject("DropMarker");
        _marker.transform.position = _ground + Vector3.up * 0.03f;

        _markerMaterial = FlatRing(_marker.transform, VfxUtil.RingTexture, Def.color, 3.2f, 1.4f);

        if (IsHazard)
        {
            // Everyone can see exactly how far "too close" is.
            _dangerMaterial = FlatRing(_marker.transform, VfxUtil.GlowTexture, Def.color,
                TreasureEffects.BlastRadius * 2f, 0.16f);
        }
    }

    static Material FlatRing(Transform parent, Texture2D texture, Color color, float size, float intensity)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Ring";
        quad.transform.SetParent(parent, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = Vector3.one * size;
        var material = VfxUtil.MakeAdditiveMaterial(texture, color, intensity);
        quad.GetComponent<MeshRenderer>().material = material;
        return material;
    }

    // ------------------------------------------------------------------ tick

    void Update()
    {
        if (_state == State.Gone)
            return;

        _phase += Time.deltaTime;

        switch (_state)
        {
            case State.Falling: TickFall(); break;
            case State.Launched: TickLaunch(); break;
            default: TickLanded(); break;
        }

        PulseBeacon();
        CollapseChute();
    }

    /// <summary>
    /// Ballistic arc from the wreckage to the floor. Hand-parameterised rather
    /// than handed to the physics engine: the landing point has to be exactly
    /// the marked, NavMesh-reachable spot bots are pathing to, and a rigidbody
    /// would scatter it into a wall or on top of cover.
    /// </summary>
    void TickLaunch()
    {
        _launchT += Time.deltaTime / LaunchSeconds;
        float t = Mathf.Clamp01(_launchT);

        Vector3 end = _ground + Vector3.up * RestHeight;
        Vector3 flat = Vector3.Lerp(_launchFrom, end, t);
        float apex = LaunchApex * 4f * t * (1f - t);   // parabola, zero at both ends
        transform.position = flat + Vector3.up * apex;

        // Tumbling end over end while it flies.
        transform.rotation = Quaternion.Euler(_launchT * 640f, _launchT * 380f, _launchT * 210f);

        if (t >= 1f)
            Land();
    }

    void TickFall()
    {
        float y = transform.position.y - FallSpeed * Time.deltaTime;

        // Re-ask what is underneath on the way down. The spawner picks a clear
        // column, but cover slides around the arena constantly, so a block can
        // arrive under a crate that is already falling. Landing on top of it is
        // right; sinking through it is what this fixes.
        if (TryFindSupport(y, Mathf.Max(0f, y - _ground.y) + 0.9f, out float surfaceY)
            && surfaceY > _ground.y + 0.05f)
            SetGroundHeight(surfaceY);

        float restY = _ground.y + RestHeight;

        // Drift under the canopy, damping to nothing so it lands on the marker.
        float sway = Mathf.Clamp01((y - restY) / SwaySettleHeight);
        transform.position = new Vector3(
            _ground.x + Mathf.Sin(_phase * 0.8f) * 0.65f * sway,
            Mathf.Max(y, restY),
            _ground.z + Mathf.Cos(_phase * 0.62f) * 0.65f * sway);
        transform.rotation = Quaternion.Euler(
            Mathf.Sin(_phase * 0.8f) * 7f * sway, _phase * 12f, Mathf.Cos(_phase * 0.62f) * 7f * sway);

        if (y <= restY)
            Land();
    }

    void Land()
    {
        _state = State.Landed;
        _landedAt = Time.time;
        transform.rotation = Quaternion.identity;
        VfxUtil.SpawnBurst(_ground + Vector3.up * 0.2f, Def.color, 10, 3f);

        if (_chute != null)
            _chuteCollapse = 0.0001f;   // starts the collapse animation
        if (_launchTrail != null)
        {
            Destroy(_launchTrail);
            _launchTrail = null;
        }

        // Announced once per crate, not once per touchdown: a crate whose cover
        // block slides away lands a second time, and the arena does not need
        // telling twice.
        if (IsHazard && !_armAnnounced)
        {
            _armAnnounced = true;
            MatchAnnouncer.Say("SCRAP MINE ARMED", Def.blurb, Def.color);
        }
    }

    void TickLanded()
    {
        // Idle float + slow spin so a landed crate still catches the eye.
        transform.position = new Vector3(_ground.x,
            _ground.y + RestHeight + Mathf.Sin(_phase * 2f) * 0.07f, _ground.z);
        if (!IsHazard)
            transform.rotation = Quaternion.Euler(0f, _phase * 42f, 0f);

        if (Time.time - _landedAt > GroundLifetime)
        {
            Expire();
            return;
        }

        // A crate that landed on a cover block is standing on something that
        // moves, and blocks slide, sink when shot, and regrow elsewhere. When
        // the support goes, finish the fall instead of hanging in mid-air.
        if (Time.time >= _nextSupportScan)
        {
            _nextSupportScan = Time.time + 0.25f;
            if (TryFindSupport(_ground.y + RestHeight, 60f, out float surfaceY)
                && surfaceY < _ground.y - 0.15f)
            {
                SetGroundHeight(surfaceY);
                _state = State.Falling;
                return;
            }
        }

        bool armed = !IsHazard || Time.time - _landedAt >= MineArmSeconds;
        if (!armed || Time.time < _nextReachScan)
            return;
        // 10 Hz is plenty: a sprinting robot covers ~0.5 m between scans against
        // a ~1.7 m reach, and this avoids a whole-scene query every frame per drop.
        _nextReachScan = Time.time + 0.1f;

        float radius = IsHazard ? MineTriggerRadius : PickupRadius;
        foreach (var shield in Object.FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.IsDown || !shield.gameObject.activeInHierarchy)
                continue;
            if (Vector3.Distance(shield.transform.position, _ground) > radius)
                continue;

            if (IsHazard)
                Detonate();
            else
                Collect(shield);
            return;
        }
    }

    void PulseBeacon()
    {
        if (_beacon == null)
            return;
        // Mines strobe urgently; supply crates breathe.
        float speed = IsHazard ? 7f : 2.4f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_phase * speed);
        _beacon.intensity = Mathf.Lerp(1.2f, IsHazard ? 4.5f : 3.2f, pulse);
        if (_markerMaterial != null)
            _markerMaterial.SetFloat("_Intensity", Mathf.Lerp(0.8f, 2f, pulse));
        if (_dangerMaterial != null)
            _dangerMaterial.SetFloat("_Intensity", Mathf.Lerp(0.10f, 0.24f, pulse));
    }

    /// <summary>Canopy folds flat over the crate after touchdown, then disappears.</summary>
    void CollapseChute()
    {
        if (_chuteCollapse <= 0f || _chute == null)
            return;
        _chuteCollapse += Time.deltaTime / 0.9f;
        float t = Mathf.Clamp01(_chuteCollapse);
        _chute.localScale = Vector3.Lerp(_chuteScale, new Vector3(1.1f, 0.04f, 1.1f), t);
        _chute.localPosition = Vector3.Lerp(Vector3.up * 1.75f, Vector3.up * 0.35f, t);
        if (t >= 1f)
        {
            Destroy(_chute.gameObject);
            _chute = null;
            _chuteCollapse = 0f;
        }
    }

    // ------------------------------------------------------------------ damage

    /// <summary>
    /// Weapon fire landed on this drop. Mines go off on the first hit — that's
    /// the safe way to clear one. Supply crates have health and can be shot out
    /// to deny them to the other team.
    /// </summary>
    public void TakeHit(float damage, Vector3 point)
    {
        if (_state == State.Gone)
            return;

        if (IsHazard)
        {
            Detonate();
            return;
        }

        _health -= damage;
        VfxUtil.ImpactBurst(point, Def.color);
        if (_health > 0f)
            return;

        VfxUtil.Explosion(transform.position, Def.color, 1.1f);
        MatchAnnouncer.Say($"{Def.displayName} DESTROYED", "shot out before anyone could reach it", Def.color);
        Vanish();
    }

    void Detonate()
    {
        if (_state == State.Gone)
            return;
        _state = State.Gone;
        TreasureEffects.Detonate(transform.position, Def);
        Vanish();
    }

    void Collect(EnergyShield picker)
    {
        _state = State.Gone;
        TreasureEffects.Apply(Def, picker);
        // Bots take a breather from shopping after a score (see AIBrain).
        picker.GetComponent<AIBrain>()?.NotifyPickedUpTreasure();
        Vanish();
    }

    void Expire()
    {
        VfxUtil.SpawnBurst(transform.position, Def.color, 12, 2.5f);
        Vanish();
    }

    /// <summary>Remove the drop (used by pickup, destruction, expiry and match end).</summary>
    public void Vanish()
    {
        _state = State.Gone;
        Active.Remove(this);
        Destroy(gameObject);
    }
}
