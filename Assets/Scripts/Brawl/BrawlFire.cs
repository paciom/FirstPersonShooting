using UnityEngine;

/// <summary>
/// The falling fire: a warning ring, a blazing comet, then a burning patch
/// that stays lit for ~30 seconds and hurts BOTH robots standing in it — a
/// piece of the arena neither side owns and both must dance around. Direct
/// hits sting extra.
///
/// The flames themselves come from <see cref="BrawlFx"/>, which prefers a
/// prefab in Resources/BrawlFx (drop a bought asset-store fire there and it
/// takes over) and otherwise builds its own on the project's additive
/// sprite pipeline.
/// </summary>
public class BrawlFire : MonoBehaviour, BrawlProps.IStrikeable
{
    const int LandDamage = 12;
    const int BurnDamage = 4;
    const float BurnTick = 0.9f;
    const float PatchRadius = 1.25f;
    const float BurnSeconds = 30f;
    const float DieDownSeconds = 4f;
    const float FallSpeed = 11f;
    /// <summary>Scale a dropped-in fire prefab to cover the burn patch.</summary>
    const float PatchScale = 1.15f;

    float _groundY;
    bool _burning;
    float _burnLeft = BurnSeconds;
    float _cyanCooldown, _magentaCooldown;
    GameObject _warning, _comet;
    ParticleSystem[] _systems;
    float[] _baseRates;
    GameObject _prefabFx;
    float _prefabCheck = -1f;
    Light _glow;
    float _flicker;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        var go = new GameObject("BrawlFire");
        go.transform.SetParent(stageRoot, false);
        float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
        go.transform.localPosition = new Vector3(x, ground, z);

        var fire = go.AddComponent<BrawlFire>();
        fire._groundY = ground;
        fire.BuildComet(ground + 10f);
        fire._warning = fire.BuildWarningRing(stageRoot, x, ground, z);
        BrawlAudio.Play(BrawlAudio.Id.Whoosh, new Vector3(x, ground + 6f, z), 0.5f);
        BrawlProps.Register(fire);
    }

    GameObject BuildWarningRing(Transform stageRoot, float x, float ground, float z)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition = new Vector3(x, ground + 0.03f, z);
        ring.transform.localScale = new Vector3(PatchRadius * 2.1f, 0.012f, PatchRadius * 2.1f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-fire-warning", new Color(1f, 0.3f, 0.12f), 2.2f);
        return ring;
    }

    void BuildComet(float y)
    {
        _comet = new GameObject("Comet");
        _comet.transform.SetParent(transform, false);
        _comet.transform.position = new Vector3(transform.position.x, y, transform.position.z);

        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(_comet.transform, false);
        core.transform.localScale = Vector3.one * 0.42f;
        core.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-fire-core", new Color(1f, 0.62f, 0.2f), 2.6f);

        // The tail streams UP behind a falling comet, so the flame rig sits
        // as-is — its own updraught is the trail.
        // The comet rides the pack's BIG fire shrunk down — its Small and
        // Medium prefabs are the ones missing materials.
        var custom = BrawlFx.TryPrefab("firecomet", _comet.transform, Vector3.zero, 0.3f);
        if (custom == null)
            BrawlFx.BuildFire(_comet.transform, 0.16f, 0.55f);

        if (!BrawlFx.HasOwnLight(custom))
        {
            var light = new GameObject("CometGlow").AddComponent<Light>();
            light.transform.SetParent(_comet.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.intensity = 2.4f;
            light.range = 8f;
        }
    }

    void BuildPatch()
    {
        _prefabFx = BrawlFx.TryPrefab("fire", transform, Vector3.zero, PatchScale);
        if (_prefabFx == null)
            BuildOwnFire();
        else
            _prefabCheck = 0.8f;   // …and prove it is really burning

        // The scorch only exists for the HAND-BUILT fire, which has no
        // ground element of its own. A bought floor fire brings its own
        // burning-ground quad, and a lit disc laid over the top of it
        // hides that quad and catches the fire's own orange light — which
        // is all a brown plate on the floor really was.
        if (_prefabFx == null)
        {
            var scorch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            scorch.name = "Scorch";
            Destroy(scorch.GetComponent<Collider>());
            scorch.transform.SetParent(transform, false);
            scorch.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            scorch.transform.localScale = new Vector3(PatchRadius * 1.7f, 0.02f, PatchRadius * 1.7f);
            scorch.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Lit("brawl-fire-scorch", new Color(0.05f, 0.04f, 0.035f), 0.15f);
        }

        // A bought effect brings its own lighting; two flicker lights on one
        // fire just washes the patch out.
        if (!BrawlFx.HasOwnLight(_prefabFx))
            EnsureGlow();
    }

    void EnsureGlow()
    {
        if (_glow != null)
            return;
        _glow = new GameObject("FireGlow").AddComponent<Light>();
        _glow.transform.SetParent(transform, false);
        _glow.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        _glow.type = LightType.Point;
        _glow.color = new Color(1f, 0.55f, 0.2f);
        _glow.intensity = 2.4f;
        _glow.range = 9f;
    }

    void BuildOwnFire()
    {
        _systems = BrawlFx.BuildFire(transform, PatchRadius);
        _baseRates = new float[_systems.Length];
        for (int i = 0; i < _systems.Length; i++)
            _baseRates[i] = _systems[i].emission.rateOverTime.constant;
    }

    /// <summary>
    /// The override gets one second to show a single particle. If it does
    /// not, it is silently broken — wrong pipeline, stripped shader, an
    /// emitter that already finished — and an INVISIBLE hazard that still
    /// burns robots is the worst outcome available, so the hand-built fire
    /// takes over. The warning names the folder, since fixing it means
    /// swapping the prefab.
    /// </summary>
    void ProvePrefabBurns()
    {
        _prefabCheck = -1f;
        if (BrawlFx.AliveParticles(_prefabFx) > 0)
            return;
        Debug.LogWarning("[BrawlFire] Resources/BrawlFx/fire renders nothing — " +
                         "falling back to the built-in flames.");
        Destroy(_prefabFx);          // takes the prefab's own light with it
        _prefabFx = null;
        BuildOwnFire();
        EnsureGlow();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (!_burning)
        {
            var p = _comet.transform.position;
            p.y -= FallSpeed * dt;
            if (p.y <= _groundY + 0.25f)
            {
                Land();
                return;
            }
            _comet.transform.position = p;
            return;
        }

        _burnLeft -= dt;
        if (_burnLeft <= 0f)
        {
            Despawn();
            return;
        }

        if (_prefabCheck > 0f)
        {
            _prefabCheck -= dt;
            if (_prefabCheck <= 0f)
                ProvePrefabBurns();
        }

        // The last stretch dies down honestly — emission and glow fade so
        // nobody is surprised when it goes out.
        float strength = Mathf.Clamp01(_burnLeft / DieDownSeconds);
        if (_systems != null)
            for (int i = 0; i < _systems.Length; i++)
            {
                var emission = _systems[i].emission;
                emission.rateOverTime = _baseRates[i] * strength;
            }
        else if (_prefabFx != null && strength < 1f)
            BrawlFx.StopEmitting(_prefabFx);

        if (_glow != null)
        {
            _flicker += dt * 11f;
            _glow.intensity = (2.4f + 0.8f * Mathf.PerlinNoise(_flicker, 0.37f)) * strength;
        }

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            _cyanCooldown -= dt;
            _magentaCooldown -= dt;
            if (Burn(controller.Cyan, _cyanCooldown))
                _cyanCooldown = BurnTick;
            if (Burn(controller.Magenta, _magentaCooldown))
                _magentaCooldown = BurnTick;
        }
    }

    void Land()
    {
        _burning = true;
        Destroy(_comet);
        if (_warning != null) { Destroy(_warning); _warning = null; }

        Vector3 at = transform.position;
        VfxUtil.Explosion(at + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.15f), 0.65f);
        BrawlAudio.Play(BrawlAudio.Id.BlastHit, at, 0.8f);

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            DirectHit(controller.Cyan, at);
            DirectHit(controller.Magenta, at);
        }
        BuildPatch();
    }

    static void DirectHit(BrawlFighter fighter, Vector3 at)
    {
        if (fighter == null)
            return;
        Vector3 gap = fighter.transform.position - at;
        if (Mathf.Abs(gap.y) > 1.6f)
            return;
        gap.y = 0f;
        if (gap.sqrMagnitude <= 1.1f * 1.1f)
            fighter.TakeAreaHit(LandDamage, at, heavy: false);
    }

    bool Burn(BrawlFighter fighter, float cooldown)
    {
        if (fighter == null || cooldown > 0f)
            return false;
        Vector3 gap = fighter.transform.position - transform.position;
        if (Mathf.Abs(gap.y) > 1.4f)
            return false;
        gap.y = 0f;
        if (gap.sqrMagnitude > PatchRadius * PatchRadius)
            return false;
        fighter.TakeAreaHit(BurnDamage, transform.position, heavy: false);
        return true;
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker) => false;

    public void Despawn()
    {
        BrawlProps.Unregister(this);
        if (_warning != null)
            Destroy(_warning);
        if (gameObject != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        BrawlProps.Unregister(this);
    }
}
