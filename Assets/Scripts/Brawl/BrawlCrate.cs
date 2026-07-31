using UnityEngine;

/// <summary>
/// The Cargo Rain crate: falls out of the sky onto the lane (its landing
/// ring warns first), then becomes terrain — stand on it, stack them, or
/// kick it. A kick skids it away from the kicker, bonking anyone in its
/// path; three kicks burst it, sometimes leaving a gift for the one who
/// broke it. Built from primitives in the stage's own materials.
/// </summary>
public class BrawlCrate : MonoBehaviour, BrawlProps.IStrikeable
{
    public const float Size = 1.1f;
    const int Hits = 3;
    const float SlideDamage = 5f;

    float _fallVelocity;
    float _slideVelocity;
    bool _resting;
    int _hitsLeft = Hits;
    BrawlFighter _kicker;
    GameObject _warning;

    public static void Spawn(Transform stageRoot, float x)
    {
        var go = new GameObject("BrawlCrate");
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(x, 9f, 0f);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = Vector3.one * Size;
        body.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Surface("brawl-crate", new Color(0.16f, 0.13f, 0.07f),
                new Color(1f, 0.75f, 0.25f), 1.2f, 0.9f);

        var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
        band.name = "Band";
        Object.Destroy(band.GetComponent<Collider>());
        band.transform.SetParent(go.transform, false);
        band.transform.localScale = new Vector3(Size + 0.04f, 0.16f, Size + 0.04f);
        band.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-crate-band", new Color(1f, 0.75f, 0.25f), 1.8f);

        var crate = go.AddComponent<BrawlCrate>();
        crate._warning = crate.BuildWarningRing(stageRoot, x);
        BrawlProps.Register(crate);
    }

    GameObject BuildWarningRing(Transform stageRoot, float x)
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "DropWarning";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(stageRoot, false);
        ring.transform.localPosition = new Vector3(x, BrawlGround.HeightAt(x) + 0.03f, 0f);
        ring.transform.localScale = new Vector3(Size * 1.3f, 0.012f, Size * 1.3f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-crate-warning", new Color(1f, 0.55f, 0.15f), 2.2f);
        return ring;
    }

    /// <summary>Centre-bottom of the crate in world space.</summary>
    float BottomY => transform.position.y - Size * 0.5f;

    void Update()
    {
        float dt = Time.deltaTime;
        var p = transform.position;

        if (!_resting)
        {
            _fallVelocity += BrawlMoveSet.Gravity * dt;
            float floor = BrawlGround.HeightAt(p.x, BottomY, this);
            float newBottom = BottomY - _fallVelocity * dt;
            if (newBottom <= floor)
            {
                transform.position = new Vector3(p.x, floor + Size * 0.5f, p.z);
                _resting = true;
                _fallVelocity = 0f;
                if (_warning != null)
                    Destroy(_warning);
                VfxUtil.SpawnBurst(transform.position - Vector3.up * (Size * 0.4f),
                    new Color(1f, 0.8f, 0.4f), 8, 3f, 0.10f);
                BrawlAudio.Play(BrawlAudio.Id.HitHeavy, transform.position, 0.5f);
                // Terrain now: fighters can stand on the lid.
                BrawlGround.Register(this, WalkableTop);
            }
            else
            {
                transform.position = new Vector3(p.x, newBottom + Size * 0.5f, p.z);
            }
            return;
        }

        // A kicked crate skids, bonking whoever it reaches.
        if (Mathf.Abs(_slideVelocity) > 0.05f)
        {
            float newX = Mathf.Clamp(p.x + _slideVelocity * dt,
                -BrawlStage.LaneHalf, BrawlStage.LaneHalf);
            transform.position = new Vector3(newX, p.y, p.z);
            _slideVelocity = Mathf.MoveTowards(_slideVelocity, 0f, 7f * dt);
            TryBonk();

            // Slid off its support (another crate): fall again.
            float under = BrawlGround.HeightAt(newX, BottomY, this);
            if (BottomY > under + 0.05f)
            {
                _resting = false;
                BrawlGround.Unregister(this);
            }
        }
    }

    float WalkableTop(float x)
    {
        if (!_resting || Mathf.Abs(x - transform.position.x) > Size * 0.5f)
            return float.NaN;
        return transform.position.y + Size * 0.5f;
    }

    void TryBonk()
    {
        var controller = BrawlController.Instance;
        if (controller == null)
            return;
        foreach (var fighter in new[] { controller.Cyan, controller.Magenta })
        {
            if (fighter == null || fighter == _kicker)
                continue;
            if (Mathf.Abs(fighter.transform.position.x - transform.position.x) > Size * 0.7f)
                continue;
            if (fighter.transform.position.y > transform.position.y + Size * 0.5f)
                continue;   // standing above it, not in its path
            var hit = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch];
            hit.damage = (int)SlideDamage;
            fighter.TakeHit(hit, _kicker != null ? _kicker : fighter.Opponent);
            _slideVelocity *= 0.3f;
            _kicker = null;   // one bonk per kick
            return;
        }
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker)
    {
        if (!_resting)
            return false;
        var closest = transform.position;
        float half = Size * 0.5f + radius;
        if (Mathf.Abs(point.x - closest.x) > half
            || Mathf.Abs(point.y - closest.y) > half)
            return false;

        _hitsLeft--;
        _kicker = attacker;
        _slideVelocity = (attacker != null ? attacker.Facing : 1f) * 6.5f;
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
        // A gift for the one who broke it, sometimes.
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
        BrawlGround.Unregister(this);
        BrawlProps.Unregister(this);
        if (_warning != null)
            Destroy(_warning);
        if (gameObject != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        BrawlGround.Unregister(this);
        BrawlProps.Unregister(this);
    }
}
