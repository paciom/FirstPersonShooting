using UnityEngine;

/// <summary>
/// Infrared: a nearly-invisible deep-red beam. The tells are heat, not light —
/// soft shimmer motes rising off the beam path like hot air, and the contact
/// point pulsing red-hot while the beam holds.
/// </summary>
public class HeatHazeProjector : Weapon
{
    public float beamRange = 20f;
    public float damagePerSecond = 42f;

    LineRenderer _line;
    ParticleSystem _shimmer;
    bool _firedThisFrame;
    float _nextGlowTime;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Heat Haze Projector";
        color = new Color(1f, 0.3f, 0.15f);
        range = beamRange;
        preferredRange = 13f;

        // The beam itself: deep red and very faint — you sense it more than see it.
        var lineGo = new GameObject("IRBeam");
        lineGo.transform.SetParent(muzzle, false);
        _line = lineGo.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.startWidth = 0.05f;
        _line.endWidth = 0.03f;
        _line.material = VfxUtil.MakeAdditiveMaterial(null, new Color(0.7f, 0.1f, 0.04f), 1.1f);
        _line.enabled = false;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;
        Vector3 end = origin + dir * beamRange;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, beamRange, ~0, QueryTriggerInteraction.Ignore)
            && hit.transform.root != ownerRoot)
        {
            end = hit.point;
            var shield = hit.transform.root.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId != TeamId)
                shield.TakeHit(damagePerSecond * Time.deltaTime, hit.point, ownerRoot);
            else
                WeaponUtil.DamageProp(hit.collider, damagePerSecond * Time.deltaTime, hit.point);

            // Contact point pulses red-hot while the beam holds.
            if (Time.time >= _nextGlowTime)
            {
                _nextGlowTime = Time.time + 0.09f;
                FlashQuad.Spawn(hit.point + hit.normal * 0.06f, VfxUtil.GlowTexture, color,
                    0.25f, 0.6f, 0.18f, 2.2f);
            }
        }

        // Heat rises: shimmer motes drifting up off random points of the beam.
        EnsureShimmer();
        float beamLength = Vector3.Distance(origin, end);
        for (int i = 0; i < 2; i++)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = origin + dir * Random.Range(0.5f, Mathf.Max(0.6f, beamLength)),
            };
            _shimmer.Emit(emit, 1);
        }

        _line.enabled = true;
        _line.SetPosition(0, origin);
        _line.SetPosition(1, end);
        FlashMuzzle(1.6f);   // subtle — the whole point is stealth heat
    }

    void EnsureShimmer()
    {
        if (_shimmer != null)
            return;
        var go = new GameObject("HeatShimmer");
        _shimmer = go.AddComponent<ParticleSystem>();
        _shimmer.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _shimmer.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(1f, 0.45f, 0.2f) * 0.9f;   // faint — it's haze, not fire
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
        main.gravityModifier = -0.35f;   // heat rises
        var emission = _shimmer.emission;
        emission.enabled = false;

        var colorOverLifetime = _shimmer.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        go.GetComponent<ParticleSystemRenderer>().material =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.PuffTexture, Color.white, 0.8f);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame && _line != null)
            _line.enabled = false;
        _firedThisFrame = false;
    }
}
