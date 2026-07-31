using UnityEngine;

/// <summary>
/// The Bouncer Ball: drops in and lopes along the lane in slow glowing
/// arcs — jump it, duck under its apex, or kick it mid-bounce to fire it
/// at the other robot. A launched ball hurts anyone who isn't its kicker
/// until it calms back down into a lazy bounce. Retires on its own.
/// </summary>
public class BrawlBall : MonoBehaviour, BrawlProps.IStrikeable
{
    const float Radius = 0.55f;
    const float Restitution = 0.72f;
    const float LazyBounce = 5.5f;
    const float LifeSeconds = 26f;

    Vector3 _velocity;   // horizontal (XZ)
    float _vy;
    float _hot;      // seconds the ball stays dangerous after a kick
    BrawlFighter _kicker;
    float _life = LifeSeconds;

    public static void Spawn(Transform stageRoot, float x, float z)
    {
        var go = new GameObject("BrawlBall");
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(x, 7.5f, z);

        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "Shell";
        Object.Destroy(shell.GetComponent<Collider>());
        shell.transform.SetParent(go.transform, false);
        shell.transform.localScale = Vector3.one * (Radius * 2f);
        shell.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-ball", new Color(0.55f, 0.35f, 1f), 1.7f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.6f, 0.4f, 1f);
        light.range = 3.5f;
        light.intensity = 1.1f;

        var ball = go.AddComponent<BrawlBall>();
        float wander = Random.Range(0f, Mathf.PI * 2f);
        ball._velocity = new Vector3(Mathf.Cos(wander), 0f, Mathf.Sin(wander)) * 1.6f;
        BrawlProps.Register(ball);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _life -= dt;
        if (_life <= 0f)
        {
            VfxUtil.SpawnBurst(transform.position, new Color(0.6f, 0.4f, 1f), 8, 3f, 0.1f);
            Despawn();
            return;
        }

        _vy -= BrawlMoveSet.Gravity * dt;
        var p = transform.position + _velocity * dt + Vector3.up * (_vy * dt);

        // The ring's edges and the floor are its walls; energy fades back
        // to the lazy bounce whether it was kicked hard or not.
        var half = BrawlStage.BoundsHalf;
        if (p.x < -half.x + Radius || p.x > half.x - Radius)
        {
            p.x = Mathf.Clamp(p.x, -half.x + Radius, half.x - Radius);
            _velocity.x = -_velocity.x * 0.85f;
        }
        if (p.z < -half.y + Radius || p.z > half.y - Radius)
        {
            p.z = Mathf.Clamp(p.z, -half.y + Radius, half.y - Radius);
            _velocity.z = -_velocity.z * 0.85f;
        }
        float floor = BrawlGround.HeightAt(p.x, p.z) + Radius;
        if (p.y < floor && _vy < 0f)
        {
            p.y = floor;
            _vy = Mathf.Max(-_vy * Restitution, LazyBounce);
            _velocity *= 0.96f;
            BrawlAudio.Play(BrawlAudio.Id.Graze, p, 0.3f);
        }
        transform.position = p;

        if (_hot > 0f)
        {
            _hot -= dt;
            TryHitFighters();
        }
    }

    void TryHitFighters()
    {
        var controller = BrawlController.Instance;
        if (controller == null)
            return;
        foreach (var fighter in new[] { controller.Cyan, controller.Magenta })
        {
            if (fighter == null || fighter == _kicker)
                continue;
            Vector3 chest = fighter.transform.position + Vector3.up * 1.0f;
            if ((chest - transform.position).sqrMagnitude > (Radius + 0.65f) * (Radius + 0.65f))
                continue;
            var hit = BrawlMoveSet.Table[BrawlMoveSet.Move.Punch];
            hit.damage = 10;
            fighter.TakeHit(hit, _kicker != null ? _kicker : fighter.Opponent);
            VfxUtil.ImpactBurst(transform.position, new Color(0.7f, 0.5f, 1f));
            _hot = 0f;
            _velocity = -_velocity * 0.5f;
            _vy = Mathf.Max(_vy, 4f);
            return;
        }
    }

    public bool Strike(Vector3 point, float radius, BrawlFighter attacker)
    {
        if ((point - transform.position).sqrMagnitude > (Radius + radius) * (Radius + radius))
            return false;
        _kicker = attacker;
        _hot = 1.6f;
        Vector3 direction = attacker != null ? attacker.FacingDir : -transform.position.normalized;
        _velocity = direction * 9.5f;
        _vy = 4.5f;
        VfxUtil.SpawnBurst(transform.position, new Color(0.7f, 0.5f, 1f), 7, 3.5f, 0.10f);
        BrawlAudio.Play(BrawlAudio.Id.Hit, transform.position, 0.8f);
        return true;
    }

    public void Despawn()
    {
        BrawlProps.Unregister(this);
        if (gameObject != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        BrawlProps.Unregister(this);
    }
}
