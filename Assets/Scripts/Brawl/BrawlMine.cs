using UnityEngine;

/// <summary>
/// The proximity mine: drops in under a warning ring, arms with a slow red
/// blink, and when anyone steps close the blink goes frantic for half a
/// second — a fair dodge window — before it pops for real damage to BOTH
/// sides in its circle. Striking it from reach detonates it early, which is
/// both the safe disposal and the dirtiest trick in the book.
/// </summary>
public class BrawlMine : MonoBehaviour, BrawlProps.IStrikeable
{
    const int Damage = 15;
    const float TriggerRadius = 1.3f;
    const float BlastRadius = 2.2f;
    const float FuseSeconds = 0.55f;
    const float ShelfLife = 30f;
    const float FallSpeed = 7f;

    float _groundY;
    bool _landed;
    bool _armed;
    float _armDelay = 0.8f;
    float _fuse = -1f;
    float _life = ShelfLife;
    float _blinkClock;
    GameObject _warning;
    MeshRenderer _lamp;
    Material _lampOn, _lampOff;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        var go = new GameObject("BrawlMine");
        go.transform.SetParent(stageRoot, false);
        float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
        go.transform.localPosition = new Vector3(x, ground + 9f, z);

        var mine = go.AddComponent<BrawlMine>();
        mine._groundY = ground;
        mine.BuildBody();
        mine._warning = BuildRing(stageRoot, x, ground, z, "brawl-mine-warning",
            new Color(1f, 0.45f, 0.2f), 1.1f);
        BrawlProps.Register(mine);
    }

    static GameObject BuildRing(Transform stageRoot, float x, float ground, float z,
        string key, Color color, float size)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Object.Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition = new Vector3(x, ground + 0.03f, z);
        ring.transform.localScale = new Vector3(size, 0.012f, size);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Emissive(key, color, 2f);
        return ring;
    }

    void BuildBody()
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Disc";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.localScale = new Vector3(0.56f, 0.09f, 0.56f);
        disc.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("brawl-mine-body", new Color(0.22f, 0.22f, 0.26f), 0.6f);

        var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lamp.name = "Lamp";
        Destroy(lamp.GetComponent<Collider>());
        lamp.transform.SetParent(transform, false);
        lamp.transform.localPosition = new Vector3(0f, 0.13f, 0f);
        lamp.transform.localScale = Vector3.one * 0.16f;
        _lamp = lamp.GetComponent<MeshRenderer>();
        _lampOn = ArenaMaterials.Emissive("brawl-mine-lamp-on", new Color(1f, 0.2f, 0.15f), 2.4f);
        _lampOff = ArenaMaterials.Lit("brawl-mine-lamp-off", new Color(0.35f, 0.1f, 0.1f), 0.4f);
        _lamp.sharedMaterial = _lampOff;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (!_landed)
        {
            var p = transform.position;
            p.y -= FallSpeed * dt;
            if (p.y <= _groundY + 0.05f)
            {
                p.y = _groundY + 0.05f;
                _landed = true;
                if (_warning != null) { Destroy(_warning); _warning = null; }
                VfxUtil.SpawnBurst(p, new Color(1f, 0.5f, 0.25f), 6, 2.2f, 0.09f);
            }
            transform.position = p;
            return;
        }

        if (!_armed)
        {
            _armDelay -= dt;
            if (_armDelay <= 0f)
            {
                _armed = true;
                BrawlAudio.Play(BrawlAudio.Id.Graze, transform.position, 0.25f);
            }
            return;
        }

        _life -= dt;
        if (_life <= 0f && _fuse < 0f)
        {
            // Timed out untriggered: de-rez, no boom.
            VfxUtil.SpawnBurst(transform.position, new Color(1f, 0.4f, 0.2f), 6, 1.8f, 0.08f);
            Despawn();
            return;
        }

        // The blink: lazy while watching, frantic on the fuse.
        _blinkClock += dt;
        float period = _fuse >= 0f ? 0.09f : 0.55f;
        if (_blinkClock >= period)
        {
            _blinkClock = 0f;
            _lamp.sharedMaterial = _lamp.sharedMaterial == _lampOn ? _lampOff : _lampOn;
        }

        if (_fuse < 0f && SomeoneClose())
        {
            _fuse = FuseSeconds;
            BrawlAudio.Play(BrawlAudio.Id.Graze, transform.position, 0.5f);
        }
        if (_fuse >= 0f)
        {
            _fuse -= dt;
            if (_fuse <= 0f)
                Detonate();
        }
    }

    bool SomeoneClose()
    {
        var controller = BrawlController.Instance;
        if (controller == null)
            return false;
        return Close(controller.Cyan) || Close(controller.Magenta);
    }

    bool Close(BrawlFighter fighter)
    {
        if (fighter == null)
            return false;
        Vector3 gap = fighter.transform.position - transform.position;
        if (Mathf.Abs(gap.y) > 1.6f)
            return false;
        gap.y = 0f;
        return gap.sqrMagnitude <= TriggerRadius * TriggerRadius;
    }

    void Detonate()
    {
        Vector3 at = transform.position;
        VfxUtil.Explosion(at + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.2f), 0.8f);
        BrawlAudio.Play(BrawlAudio.Id.BlastHit, at, 1f);
        var camera = BrawlController.Instance != null ? BrawlController.Instance.Camera : null;
        if (camera != null)
            camera.Kick(0.14f);

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            Blast(controller.Cyan, at);
            Blast(controller.Magenta, at);
        }
        Despawn();
    }

    static void Blast(BrawlFighter fighter, Vector3 at)
    {
        if (fighter == null)
            return;
        Vector3 gap = fighter.transform.position - at;
        if (Mathf.Abs(gap.y) > 1.8f)
            return;
        gap.y = 0f;
        if (gap.sqrMagnitude <= BlastRadius * BlastRadius)
            fighter.TakeAreaHit(Damage, at, heavy: true);
    }

    /// <summary>A kick or punch sets it off early — range is the safety.</summary>
    public bool Strike(Vector3 point, float radius, BrawlFighter attacker)
    {
        if (!_landed)
            return false;
        Vector3 gap = point - transform.position;
        float reach = radius + 0.45f;
        if (gap.sqrMagnitude > reach * reach)
            return false;
        Detonate();
        return true;
    }

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
