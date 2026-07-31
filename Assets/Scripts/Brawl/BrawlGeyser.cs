using UnityEngine;

/// <summary>
/// The steam geyser: a floor vent on its own rhythm — quiet, then a HISS of
/// warning wisps, then a white column that pops anyone standing on it
/// straight into the air (no damage; it's a launcher, not a trap). Crates
/// caught over the vent go flying too.
/// </summary>
public class BrawlGeyser : MonoBehaviour
{
    const float VentRadius = 1.05f;
    const float WarnSeconds = 0.9f;
    const float EruptSeconds = 0.5f;
    const float LaunchVelocity = 10.5f;

    float _clock;
    float _idleSpan;
    ParticleSystem _steam;
    ParticleSystem.EmissionModule _steamEmission;
    Transform _ringGlow;
    enum Stage { Idle, Warning, Erupting }
    Stage _stage = Stage.Idle;

    public static void Spawn(Transform stageRoot, float x, float z, float phase)
    {
        var go = new GameObject("BrawlGeyser");
        go.transform.SetParent(stageRoot, false);
        float ground = BrawlGround.HeightAt(x, z, aboveY: 30f);
        go.transform.localPosition = new Vector3(x, ground, z);

        var geyser = go.AddComponent<BrawlGeyser>();
        geyser._idleSpan = Random.Range(4.5f, 7.5f);
        geyser._clock = geyser._idleSpan * phase;   // staggered starts
        geyser.BuildVent();
    }

    void BuildVent()
    {
        // Flush floor dressing — no colliders anywhere, the floor is the floor.
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plate.name = "Plate";
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(transform, false);
        plate.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        plate.transform.localScale = new Vector3(VentRadius * 2.1f, 0.025f, VentRadius * 2.1f);
        plate.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("brawl-geyser-plate", new Color(0.30f, 0.33f, 0.38f), 0.55f);

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "RingGlow";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.055f, 0f);
        ring.transform.localScale = new Vector3(VentRadius * 1.5f, 0.012f, VentRadius * 1.5f);
        ring.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-geyser-ring", new Color(0.75f, 0.92f, 1f), 1.3f);
        _ringGlow = ring.transform;

        _steam = BrawlFireVfx.MakeSystem(transform, "Steam",
            new Color(0.92f, 0.96f, 1f, 0.5f), new Color(0.75f, 0.8f, 0.9f, 0f),
            additive: false, rate: 0f, size: 0.55f, grow: 2.2f,
            speed: 5.5f, lifetime: 0.8f, radius: VentRadius * 0.6f);
        _steamEmission = _steam.emission;
    }

    void Update()
    {
        _clock += Time.deltaTime;
        switch (_stage)
        {
            case Stage.Idle:
                if (_clock >= _idleSpan)
                {
                    _stage = Stage.Warning;
                    _clock = 0f;
                    _steamEmission.rateOverTime = 14f;   // the wisps
                    BrawlAudio.Play(BrawlAudio.Id.Whoosh, transform.position, 0.5f);
                }
                break;

            case Stage.Warning:
                _ringGlow.localScale = Vector3.one * (VentRadius * (1.5f + 0.25f *
                    Mathf.PingPong(Time.time * 6f, 1f)));
                _ringGlow.localScale = new Vector3(_ringGlow.localScale.x, 0.012f, _ringGlow.localScale.z);
                if (_clock >= WarnSeconds)
                {
                    _stage = Stage.Erupting;
                    _clock = 0f;
                    Erupt();
                }
                break;

            case Stage.Erupting:
                if (_clock >= EruptSeconds)
                {
                    _stage = Stage.Idle;
                    _clock = 0f;
                    _idleSpan = Random.Range(4.5f, 7.5f);
                    _steamEmission.rateOverTime = 0f;
                }
                break;
        }
    }

    void Erupt()
    {
        _steamEmission.rateOverTime = 90f;
        _steam.Emit(30);
        BrawlAudio.Play(BrawlAudio.Id.Whoosh, transform.position, 0.9f);
        VfxUtil.SpawnBurst(transform.position + Vector3.up * 0.4f,
            new Color(0.9f, 0.97f, 1f), 10, 4.5f, 0.14f);

        var controller = BrawlController.Instance;
        if (controller != null)
        {
            Pop(controller.Cyan);
            Pop(controller.Magenta);
        }

        // Crates over the vent ride the column.
        foreach (var overlap in Physics.OverlapSphere(transform.position + Vector3.up * 0.6f,
                     VentRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            var body = overlap.attachedRigidbody;
            if (body != null)
                body.AddForce(Vector3.up * 6.5f, ForceMode.VelocityChange);
        }
    }

    void Pop(BrawlFighter fighter)
    {
        if (fighter == null)
            return;
        Vector3 gap = fighter.transform.position - transform.position;
        if (gap.y < -0.5f || gap.y > 1.2f)
            return;   // not standing at the vent's level
        gap.y = 0f;
        if (gap.sqrMagnitude <= VentRadius * VentRadius)
            fighter.LaunchUp(LaunchVelocity);
    }
}
