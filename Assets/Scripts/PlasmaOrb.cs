using UnityEngine;

/// <summary>
/// Slow, glowing, gravity-arced plasma projectile. On impact (or timeout) it
/// bursts, draining every opposing shield inside a splash radius and popping a
/// big shockwave-ring explosion — the Plasma Lobber's signature area moment.
/// </summary>
public class PlasmaOrb : MonoBehaviour
{
    Vector3 _velocity;
    float _damage;
    float _splashRadius;
    Color _color;
    int _teamId;
    Transform _ownerRoot;
    float _age;
    float _maxLifetime = 5f;
    bool _spent;

    /// <summary>Shared with PlasmaLobber's trajectory solver — keep in sync.</summary>
    public const float Gravity = -14f;

    public static PlasmaOrb Spawn(Vector3 position, Vector3 velocity, float damage, float splashRadius,
        Color color, int teamId, Transform ownerRoot)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "PlasmaOrb";
        Object.Destroy(go.GetComponent<Collider>());   // movement is manual
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.4f;
        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(color, 3.5f);

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.3f;
        trail.endWidth = 0f;
        trail.material = VfxUtil.MakeGlowMaterial(color, 2f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 3f;
        light.range = 5f;

        var orb = go.AddComponent<PlasmaOrb>();
        orb._velocity = velocity;
        orb._damage = damage;
        orb._splashRadius = splashRadius;
        orb._color = color;
        orb._teamId = teamId;
        orb._ownerRoot = ownerRoot;
        return orb;
    }

    void Update()
    {
        if (_spent)
            return;

        _age += Time.deltaTime;
        _velocity += Vector3.up * (Gravity * Time.deltaTime);

        Vector3 step = _velocity * Time.deltaTime;
        float dist = step.magnitude;
        if (Physics.Raycast(transform.position, step.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
            && hit.transform.root != _ownerRoot)
        {
            Burst(hit.point + hit.normal * 0.1f);
            return;
        }

        transform.position += step;
        if (_age > _maxLifetime)
            Burst(transform.position);
    }

    void Burst(Vector3 position)
    {
        _spent = true;
        VfxUtil.Explosion(position, _color, 1.6f);

        // Splash: every opposing shield within radius takes damage, falling off
        // toward the edge.
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == _teamId || shield.IsDown)
                continue;
            float d = Vector3.Distance(position, shield.transform.position + Vector3.up * 1f);
            if (d <= _splashRadius)
            {
                float falloff = 1f - (d / _splashRadius);
                shield.TakeHit(_damage * falloff, shield.transform.position + Vector3.up * 1f, _ownerRoot);
            }
        }

        // Splash also blasts cover blocks and airdrop crates in range.
        foreach (var col in Physics.OverlapSphere(position, _splashRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            float d = Vector3.Distance(position, col.transform.position);
            WeaponUtil.DamageProp(col, _damage * Mathf.Clamp01(1f - d / _splashRadius), position);
        }

        Destroy(gameObject);
    }
}
