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

    // Case body, in metres. The cross has to sit just proud of each face,
    // so every offset here is derived from these rather than hand-typed.
    static readonly Vector3 CaseSize = new Vector3(0.66f, 0.48f, 0.52f);

    /// <summary>
    /// A case that reads as a MEDKIT from any angle. Three rules the first
    /// version broke, which is why it landed as a plain white box:
    ///
    /// 1. The cross goes on EVERY face, not one. The case spins, so a
    ///    single-sided decal is invisible three quarters of the time.
    /// 2. A white cube has no silhouette. The dark seam band and the lid
    ///    handle are what say "carried box" rather than "sugar cube".
    /// 3. Bloom eats white. The shell sits at 0.86 rather than 0.94 and the
    ///    cross emits at 1.8, well under the 2.5 whiteout line — a green
    ///    cross on white beats a white blob on white.
    /// </summary>
    void BuildCase()
    {
        _case = new GameObject("Case").transform;
        _case.SetParent(transform, false);

        var shell = ArenaMaterials.Lit("brawl-kit-shell", new Color(0.86f, 0.89f, 0.92f), 0.35f);
        var trim = ArenaMaterials.Lit("brawl-kit-trim", new Color(0.09f, 0.11f, 0.15f), 0.45f);
        var cross = ArenaMaterials.Emissive("brawl-kit-cross", new Color(0.25f, 1f, 0.5f), 1.8f);

        Part(_case, Vector3.zero, CaseSize, shell, "Shell");
        // The seam where a case opens, and the handle it is carried by. The
        // seam rides LOW: through the middle it cuts the cross in half.
        Part(_case, new Vector3(0f, -0.13f, 0f),
             new Vector3(CaseSize.x + 0.015f, 0.05f, CaseSize.z + 0.015f), trim, "Seam");
        Part(_case, new Vector3(0f, CaseSize.y * 0.5f + 0.03f, 0f),
             new Vector3(0.24f, 0.06f, 0.07f), trim, "Handle");

        // Four side crosses plus one on the lid: whichever way it turns, and
        // from the high spectator camera, the cross is facing someone.
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
        {
            var face = new GameObject("CrossFace").transform;
            face.SetParent(_case, false);
            face.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // After the yaw, the face's local -Z points at either the Z or
            // the X side of the box, so the stand-off differs per axis.
            float outward = (Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) < 0.5f
                             ? CaseSize.z : CaseSize.x) * 0.5f + 0.012f;
            Part(face, new Vector3(0f, 0.06f, -outward), new Vector3(0.28f, 0.09f, 0.03f), cross, "Bar");
            Part(face, new Vector3(0f, 0.06f, -outward), new Vector3(0.09f, 0.26f, 0.03f), cross, "Bar");
        }
        float lid = CaseSize.y * 0.5f + 0.012f;
        Part(_case, new Vector3(0f, lid, 0f), new Vector3(0.30f, 0.03f, 0.10f), cross, "LidBar");
        Part(_case, new Vector3(0f, lid, 0f), new Vector3(0.10f, 0.03f, 0.28f), cross, "LidBar");

        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(_case, false);
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 1f, 0.55f);
        glow.intensity = 1.1f;
        glow.range = 3.2f;
    }

    static void Part(Transform parent, Vector3 position, Vector3 scale, Material material, string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
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
