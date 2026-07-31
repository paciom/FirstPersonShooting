using UnityEngine;

/// <summary>
/// The air-dropped repair kit: a warning ring, a falling white case with a
/// green cross, then a bobbing glow on the ground that patches up whoever
/// reaches it first. One in the world at a time — it's a PRIZE, and both
/// sides (and both AIs) know to want it.
/// </summary>
public class BrawlRepairKit : MonoBehaviour, BrawlProps.IStrikeable
{
    public const float HealAmount = 30f;
    const float CollectRadius = 0.95f;
    const float ShelfLife = 12f;
    const float FallSpeed = 7f;

    /// <summary>The kit currently in the world, if any — the brains peek.</summary>
    public static BrawlRepairKit Active { get; private set; }

    float _groundY;
    bool _landed;
    float _life = ShelfLife;
    GameObject _warning;
    Transform _case;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        var go = new GameObject("BrawlRepairKit");
        go.transform.SetParent(stageRoot, false);
        float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
        go.transform.localPosition = new Vector3(x, ground + 9f, z);

        var kit = go.AddComponent<BrawlRepairKit>();
        kit._groundY = ground;
        kit.BuildCase();
        kit._warning = kit.BuildWarningRing(stageRoot, x, ground, z);
        Active = kit;
        BrawlProps.Register(kit);
    }

    void BuildCase()
    {
        _case = new GameObject("Case").transform;
        _case.SetParent(transform, false);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(_case, false);
        body.transform.localScale = new Vector3(0.62f, 0.44f, 0.46f);
        body.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("brawl-kit-case", new Color(0.92f, 0.94f, 0.96f), 0.5f);

        var cross = ArenaMaterials.Emissive("brawl-kit-cross", new Color(0.25f, 1f, 0.5f), 2.2f);
        foreach (var scale in new[] { new Vector3(0.40f, 0.13f, 0.05f), new Vector3(0.13f, 0.34f, 0.05f) })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(_case, false);
            bar.transform.localPosition = new Vector3(0f, 0.02f, -0.24f);
            bar.transform.localScale = scale;
            bar.GetComponent<MeshRenderer>().sharedMaterial = cross;
        }

        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(_case, false);
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 1f, 0.55f);
        glow.intensity = 1.6f;
        glow.range = 4f;
    }

    GameObject BuildWarningRing(Transform stageRoot, float x, float ground, float z)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition = new Vector3(x, ground + 0.03f, z);
        ring.transform.localScale = new Vector3(1.3f, 0.012f, 1.3f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-kit-warning", new Color(0.3f, 1f, 0.55f), 2f);
        return ring;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (!_landed)
        {
            var p = transform.position;
            p.y -= FallSpeed * dt;
            if (p.y <= _groundY + 0.24f)
            {
                p.y = _groundY + 0.24f;
                _landed = true;
                if (_warning != null) { Destroy(_warning); _warning = null; }
                VfxUtil.SpawnBurst(p, new Color(0.4f, 1f, 0.6f), 8, 2.5f, 0.10f);
                BrawlAudio.Play(BrawlAudio.Id.Jump, p, 0.4f);
            }
            transform.position = p;
            return;
        }

        // Bob and slowly turn: unmistakably a pickup.
        _case.localPosition = new Vector3(0f, 0.07f * Mathf.Sin(Time.time * 3.2f), 0f);
        _case.localRotation = Quaternion.Euler(0f, Time.time * 70f, 0f);

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            TryCollect(controller.Cyan);
            TryCollect(controller.Magenta);
        }

        _life -= dt;
        if (_life <= 0f)
        {
            // Nobody wanted it: de-rez quietly.
            VfxUtil.SpawnBurst(transform.position, new Color(0.4f, 1f, 0.6f), 6, 1.8f, 0.08f);
            Despawn();
        }
    }

    void TryCollect(BrawlFighter fighter)
    {
        if (fighter == null || fighter.Health <= 0f)
            return;
        Vector3 gap = fighter.transform.position - transform.position;
        gap.y = 0f;
        if (gap.sqrMagnitude > CollectRadius * CollectRadius)
            return;
        fighter.Heal(HealAmount);
        BrawlAudio.PlayFlat(BrawlAudio.Id.ChargeReady, 0.6f);
        Despawn();
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker) => false;

    public void Despawn()
    {
        BrawlProps.Unregister(this);
        if (Active == this)
            Active = null;
        if (_warning != null)
            Destroy(_warning);
        if (gameObject != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        BrawlProps.Unregister(this);
        if (Active == this)
            Active = null;
    }
}
