using UnityEngine;

/// <summary>
/// A glowing laser projectile. Moves by raycasting each frame's travel segment
/// (no tunneling at high speed), damages EnergyShields on the opposing team,
/// and pops a spark burst on impact.
/// </summary>
public class LaserBolt : MonoBehaviour
{
    public float speed;
    public float damage;
    public float maxLifetime = 3f;
    public Color color;
    public int teamId;
    public Transform ownerRoot;

    Vector3 _direction;
    float _age;

    public static LaserBolt Spawn(Vector3 position, Vector3 direction, float speed, float damage,
        Color color, int teamId, Transform ownerRoot)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "LaserBolt";
        Object.Destroy(go.GetComponent<Collider>());   // movement uses raycasts

        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(0.08f, 0.35f, 0.08f);

        go.GetComponent<MeshRenderer>().material = VfxUtil.MakeGlowMaterial(color, 4f);

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.15f;
        trail.startWidth = 0.08f;
        trail.endWidth = 0f;
        trail.material = VfxUtil.MakeGlowMaterial(color, 2f);

        // Small light so bolts paint the floor and walls as they fly.
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 2.2f;
        light.range = 3.5f;

        var bolt = go.AddComponent<LaserBolt>();
        bolt._direction = direction.normalized;
        bolt.speed = speed;
        bolt.damage = damage;
        bolt.color = color;
        bolt.teamId = teamId;
        bolt.ownerRoot = ownerRoot;
        return bolt;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age > maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 direction = _direction;
        float step = speed * Time.deltaTime;
        if (Physics.Raycast(transform.position, direction, out RaycastHit hit, step, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.root == ownerRoot)
            {
                // Skip the shooter's own colliders and keep flying.
                transform.position += direction * step;
                return;
            }

            var shield = hit.transform.root.GetComponent<EnergyShield>();
            if (shield != null && shield.teamId != teamId)
                shield.TakeHit(damage, hit.point, ownerRoot);
            else
                WeaponUtil.DamageProp(hit.collider, damage, hit.point);

            VfxUtil.ImpactBurst(hit.point + hit.normal * 0.05f, color);
            Destroy(gameObject);
            return;
        }

        transform.position += direction * step;
    }
}
