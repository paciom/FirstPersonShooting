using UnityEngine;

/// <summary>
/// The Cargo Rain crate, now an honest Rigidbody: it drops, BOUNCES,
/// tumbles off robots and other crates (constrained to the fight plane —
/// z locked, spin around z only), and only once it settles does it become
/// standable terrain. A kick is an impulse with spin; three kicks burst
/// it, sometimes leaving a gift for the breaker. Robots carry a solid
/// bumper capsule for it to carom off — clonking one costs a little
/// health, whichever direction the crate arrived from.
/// </summary>
public class BrawlCrate : MonoBehaviour, BrawlProps.IStrikeable
{
    public const float Size = 1.1f;
    const int Hits = 3;
    const int BonkDamage = 5;
    const int IgnoreRaycastLayer = 2;

    static PhysicsMaterial _bouncy;

    Rigidbody _body;
    Collider _box;
    int _hitsLeft = Hits;
    BrawlFighter _kicker;
    float _kickerGrace;
    float _bonkCooldown;
    float _settleTimer;
    bool _settled;
    GameObject _warning;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        if (_bouncy == null)
            _bouncy = new PhysicsMaterial("brawl-crate")
            {
                bounciness = 0.42f,
                dynamicFriction = 0.55f,
                staticFriction = 0.6f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };

        var go = new GameObject("BrawlCrate");
        go.layer = IgnoreRaycastLayer;   // not terrain until it settles
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(x, 9f, z);
        go.transform.localRotation = Quaternion.Euler(
            Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Body";
        Object.Destroy(visual.GetComponent<Collider>());
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = Vector3.one * Size;
        visual.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Surface("brawl-crate", new Color(0.16f, 0.13f, 0.07f),
                new Color(1f, 0.75f, 0.25f), 1.2f, 0.9f);
        var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
        band.name = "Band";
        Object.Destroy(band.GetComponent<Collider>());
        band.transform.SetParent(go.transform, false);
        band.transform.localScale = new Vector3(Size + 0.04f, 0.16f, Size + 0.04f);
        band.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-crate-band", new Color(1f, 0.75f, 0.25f), 1.8f);

        var box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one * Size;
        box.material = _bouncy;

        var body = go.AddComponent<Rigidbody>();
        body.mass = 3f;
        // A plane fight gets full 3D tumble — no frozen axes.
        body.angularVelocity = new Vector3(
            Random.Range(-2f, 2f), Random.Range(-1f, 1f), Random.Range(-2f, 2f));

        var crate = go.AddComponent<BrawlCrate>();
        crate._body = body;
        crate._box = box;
        crate._warning = crate.BuildWarningRing(stageRoot, x, z);
        BrawlProps.Register(crate);
    }

    GameObject BuildWarningRing(Transform stageRoot, float x, float z)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition =
            new Vector3(x, BrawlGround.HeightAt(x, z) + 0.03f, z);
        ring.transform.localScale = new Vector3(Size * 1.3f, 0.012f, Size * 1.3f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-crate-warning", new Color(1f, 0.55f, 0.15f), 2.2f);
        return ring;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _kickerGrace -= dt;
        _bonkCooldown -= dt;

        // Settled = slow enough for long enough. Only a settled crate is
        // on the default layer, where the terrain probe (and the camera)
        // can see it — nobody stands on a box mid-bounce.
        float agitation = _body.linearVelocity.magnitude
                          + _body.angularVelocity.magnitude * 0.3f;
        _settleTimer = agitation < 0.25f ? _settleTimer + dt : 0f;
        bool settled = _settleTimer > 0.35f;
        if (settled != _settled)
        {
            _settled = settled;
            gameObject.layer = settled ? 0 : IgnoreRaycastLayer;
        }

        // Fell off the world somehow: retire quietly.
        if (transform.position.y < -4f)
            Despawn();
    }

    void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;

        var bumper = collision.collider.GetComponentInParent<BrawlBodyBumper>();
        if (bumper != null && bumper.Owner != null)
        {
            // Clonk: a crate arriving with real speed costs a little
            // health — whether it fell out of the sky or got kicked over.
            bool isProtectedKicker = bumper.Owner == _kicker && _kickerGrace > 0f;
            if (impact > 2.5f && _bonkCooldown <= 0f && !isProtectedKicker)
            {
                _bonkCooldown = 0.7f;
                var hit = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch];
                hit.damage = BonkDamage;
                bumper.Owner.TakeHit(hit,
                    _kicker != null && _kicker != bumper.Owner ? _kicker : bumper.Owner.Opponent);
            }
            return;
        }

        // First touchdown clears the warning and puffs the dust.
        if (_warning != null)
        {
            Destroy(_warning);
            _warning = null;
            VfxUtil.SpawnBurst(transform.position - Vector3.up * (Size * 0.4f),
                new Color(1f, 0.8f, 0.4f), 8, 3f, 0.10f);
            BrawlAudio.Play(BrawlAudio.Id.HitHeavy, transform.position, 0.5f);
        }
        else if (impact > 3f && _bonkCooldown <= 0f)
        {
            _bonkCooldown = 0.4f;
            BrawlAudio.Play(BrawlAudio.Id.Graze, transform.position, 0.4f);
        }
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker)
    {
        if (_box == null)
            return false;
        if ((_box.ClosestPoint(point) - point).sqrMagnitude > radius * radius)
            return false;

        _hitsLeft--;
        _kicker = attacker;
        _kickerGrace = 0.6f;
        Vector3 direction = attacker != null ? attacker.FacingDir : Vector3.right;
        // The kick is an impulse with spin — the box TUMBLES away.
        _body.linearVelocity = direction * 7f + Vector3.up * 3.2f;
        _body.angularVelocity = Vector3.Cross(direction, Vector3.up) * -9f;
        VfxUtil.SpawnBurst(point, new Color(1f, 0.8f, 0.4f), 6, 3f, 0.09f);
        BrawlAudio.Play(BrawlAudio.Id.Hit, point, 0.7f);

        if (_hitsLeft <= 0)
            Burst(attacker);
        return true;
    }

    void Burst(BrawlFighter breaker)
    {
        VfxUtil.Explosion(transform.position, new Color(1f, 0.8f, 0.35f), 0.5f);
        BrawlAudio.Play(BrawlAudio.Id.BlastHit, transform.position, 0.8f);
        if (breaker != null && Random.value < 0.35f)
        {
            breaker.GrantPickup();
            VfxUtil.SpawnBurst(breaker.transform.position + Vector3.up * 1.4f,
                new Color(0.4f, 1f, 0.6f), 10, 2.5f, 0.12f);
        }
        Despawn();
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
