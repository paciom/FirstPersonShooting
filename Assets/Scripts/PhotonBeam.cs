using UnityEngine;

/// <summary>
/// Continuous short-range beam that melts shields up close. Renders a solid
/// glowing beam with sparks at the contact point and drains shield every frame
/// it connects. Great on camera (a steady line of light) and a natural
/// close-quarters counterpart to the ranged weapons.
/// </summary>
public class PhotonBeam : Weapon
{
    [Header("Photon Beam")]
    public float beamRange = 13f;
    public float damagePerSecond = 55f;
    public float beamWidth = 0.14f;

    LineRenderer _line;
    ParticleSystem _contactSparks;
    bool _firedThisFrame;

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Photon Beam";
        range = beamRange;
        preferredRange = 9f;

        var lineGo = new GameObject("Beam");
        lineGo.transform.SetParent(muzzle, false);
        _line = lineGo.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.startWidth = beamWidth;
        _line.endWidth = beamWidth * 0.6f;
        _line.numCapVertices = 4;
        _line.material = VfxUtil.MakeAdditiveMaterial(null, color, 3.5f);
        _line.enabled = false;
    }

    public override void TryFire(Vector3 direction)
    {
        _firedThisFrame = true;
        Vector3 origin = muzzle.position;
        Vector3 dir = direction.normalized;

        Vector3 end = origin + dir * beamRange;
        var hits = Physics.RaycastAll(origin, dir, beamRange, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.root == ownerRoot)
                continue;   // don't stop on the shooter
            end = hit.point;

            var shield = hit.transform.root.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId != TeamId)
                shield.TakeHit(damagePerSecond * Time.deltaTime, hit.point, ownerRoot);
            else
                WeaponUtil.DamageProp(hit.collider, damagePerSecond * Time.deltaTime, hit.point);

            EmitContactSparks(hit.point);
            break;
        }

        _line.enabled = true;
        _line.SetPosition(0, origin);
        _line.SetPosition(1, end);
        FlashMuzzle(2.5f);
    }

    void EmitContactSparks(Vector3 point)
    {
        if (_contactSparks == null)
        {
            var go = new GameObject("BeamSparks");
            _contactSparks = go.AddComponent<ParticleSystem>();
            _contactSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _contactSparks.main;
            main.playOnAwake = false;
            main.startLifetime = 0.25f;
            main.startSpeed = 3.5f;
            main.startSize = 0.12f;
            main.startColor = color * 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _contactSparks.emission;
            emission.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, Color.white, 1.6f);
        }
        _contactSparks.transform.position = point;
        _contactSparks.Emit(2);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame && _line != null)
            _line.enabled = false;
        _firedThisFrame = false;
    }
}
