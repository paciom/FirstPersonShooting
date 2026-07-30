using UnityEngine;

/// <summary>
/// The PHOTON BLAST in flight: a glowing lance that rides the lane at chest
/// height until it finds the other robot or the stage ends. Damage and the
/// knockdown ride BrawlMoveSet's Blast row; blocking eats it like any other
/// hit (TakeHit already knows). Parented under the stage root so a teardown
/// mid-flight takes it along.
/// </summary>
public class BrawlBolt : MonoBehaviour
{
    const float Speed = 14f;
    const float LifeSeconds = 1.6f;

    BrawlFighter _shooter;
    BrawlFighter _target;
    Color _tint;
    float _direction;
    float _life;
    bool _spent;

    public static void Fire(BrawlFighter shooter, BrawlFighter target, Color tint)
    {
        var go = new GameObject("PhotonBlast");
        go.transform.SetParent(shooter.transform.parent, false);
        go.transform.position = shooter.transform.position
            + new Vector3(shooter.Facing * 0.7f, 1.15f, 0f);

        var bolt = go.AddComponent<BrawlBolt>();
        bolt._shooter = shooter;
        bolt._target = target;
        bolt._tint = tint;
        bolt._direction = shooter.Facing;
        bolt._life = LifeSeconds;

        // Energy weapon, so the glow may run hot (the bloom rule's one
        // sanctioned exception) — a stretched core with a light around it.
        var core = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        core.name = "Core";
        Object.Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(go.transform, false);
        core.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        core.transform.localScale = new Vector3(0.20f, 0.42f, 0.20f);
        core.GetComponent<MeshRenderer>().sharedMaterial =
            VfxUtil.MakeGlowMaterial(tint, 3.2f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = tint;
        light.range = 4.5f;
        light.intensity = 2.2f;

        VfxUtil.SpawnBurst(go.transform.position, tint, 8, 3f, 0.10f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _life -= dt;
        if (_life <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += new Vector3(_direction * Speed * dt, 0f, 0f);

        if (_spent || _target == null)
            return;
        if (_target.Phase == BrawlFighter.State.KO
            || _target.Phase == BrawlFighter.State.Knockdown)
            return;   // sails over a downed robot — no wake-up lasers

        float gap = Mathf.Abs(_target.transform.position.x - transform.position.x);
        bool inHeight = _target.transform.position.y < 1.3f;
        if (gap > 0.55f || !inHeight)
            return;

        _spent = true;
        _target.TakeHit(BrawlMoveSet.Table[BrawlMoveSet.Move.Blast], _shooter);
        VfxUtil.ImpactBurst(transform.position, _tint);
        Destroy(gameObject);
    }
}
