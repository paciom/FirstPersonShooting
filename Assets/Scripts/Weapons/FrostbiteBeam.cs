using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cryogenic: a pale-blue beam that slows its target more the longer it holds
/// on — keep it connected and they freeze solid inside a crackling ice shell.
/// </summary>
public class FrostbiteBeam : Weapon
{
    public float beamRange = 15f;
    public float damagePerSecond = 26f;
    public float secondsToFreeze = 1.4f;

    LineRenderer _lineCore;
    LineRenderer _lineHalo;
    bool _firedThisFrame;
    readonly Dictionary<Transform, float> _exposure = new Dictionary<Transform, float>();

    protected override void Awake()
    {
        base.Awake();
        if (weaponName == "Weapon") weaponName = "Frostbite Beam";
        color = new Color(0.45f, 0.75f, 1f);   // saturated ice-blue; pale blue whites out
        range = beamRange;
        preferredRange = 10f;

        // Two-layer beam: thin bright core inside a wide clearly-blue halo.
        _lineCore = MakeBeamLine("FrostBeamCore", 0.04f, 0.02f, Color.white, 2f);
        _lineHalo = MakeBeamLine("FrostBeamHalo", 0.2f, 0.1f, color, 1.1f);
    }

    LineRenderer MakeBeamLine(string name, float startWidth, float endWidth, Color lineColor, float intensity)
    {
        var lineGo = new GameObject(name);
        lineGo.transform.SetParent(muzzle, false);
        var line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.material = VfxUtil.MakeAdditiveMaterial(null, lineColor, intensity);
        line.enabled = false;
        return line;
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
            {
                shield.TakeHit(damagePerSecond * Time.deltaTime, hit.point, ownerRoot);

                var root = shield.transform.root;
                _exposure.TryGetValue(root, out float heldFor);
                heldFor += Time.deltaTime;
                _exposure[root] = heldFor;

                var fx = StatusEffects.Get(root);
                if (heldFor >= secondsToFreeze)
                {
                    fx?.ApplyFreeze(1.3f);                    // frozen solid!
                    VfxUtil.ImpactBurst(WeaponUtil.Center(shield), Color.white);
                    _exposure[root] = 0f;
                }
                else
                {
                    // Frost creeps: deeper exposure = harder slow.
                    fx?.ApplySlow(Mathf.Lerp(0.75f, 0.3f, heldFor / secondsToFreeze), 0.4f);
                }

                // Snowflake glints at the contact point.
                if (Random.value < 0.3f)
                    VfxUtil.SpawnBurst(hit.point, Color.white, 2, 1.5f, 0.08f);
            }
        }

        SetBeam(_lineCore, origin, end);
        SetBeam(_lineHalo, origin, end);
        FlashMuzzle(2.5f);
    }

    static void SetBeam(LineRenderer line, Vector3 from, Vector3 to)
    {
        line.enabled = true;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!_firedThisFrame)
        {
            if (_lineCore != null) _lineCore.enabled = false;
            if (_lineHalo != null) _lineHalo.enabled = false;
        }
        _firedThisFrame = false;
    }
}
