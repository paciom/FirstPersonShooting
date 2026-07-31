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

    float _vx;
    float _vy;
    float _hot;      // seconds the ball stays dangerous after a kick
    BrawlFighter _kicker;
    float _life = LifeSeconds;

    public static void Spawn(Transform stageRoot, float x)
    {
        var go = new GameObject("BrawlBall");
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(x, 7.5f, 0f);

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
        ball._vx = Random.value < 0.5f ? 1.6f : -1.6f;
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
        var p = transform.position + new Vector3(_vx * dt, _vy * dt, 0f);

        // The lane ends and the floor are its walls; energy fades back to
        // the lazy bounce whether it was kicked hard or not.
        float half = BrawlStage.CurrentLaneHalf;
        if (p.x < -half + Radius || p.x > half - Radius)
        {
            p.x = Mathf.Clamp(p.x, -half + Radius, half - Radius);
            _vx = -_vx * 0.85f;
        }
        float floor = BrawlGround.HeightAt(p.x) + Radius;
        if (p.y < floor && _vy < 0f)
        {
            p.y = floor;
            _vy = Mathf.Max(-_vy * Restitution, LazyBounce);
            _vx *= 0.96f;
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
            _vx = -_vx * 0.5f;
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
        float direction = attacker != null ? attacker.Facing : Mathf.Sign(-transform.position.x);
        _vx = direction * 9.5f;
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
