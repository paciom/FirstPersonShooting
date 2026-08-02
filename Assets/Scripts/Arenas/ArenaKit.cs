using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// The shared vocabulary every arena is built from. An arena's Build() gets one
/// of these and lays out geometry with it; nothing else in an arena definition
/// should be touching GameObject.CreatePrimitive directly.
///
/// The ramp and stair helpers enforce the limits that decide whether bots can
/// actually use a level, because a staircase the NavMesh bake silently drops is
/// indistinguishable from one that works until you watch a bot refuse to climb
/// it. See MinRampWidth / MaxRampAngle.
/// </summary>
public class ArenaKit
{
    /// <summary>
    /// NavMeshAgent radius is 0.4, so anything narrower than two radii plus
    /// margin bakes as impassable.
    /// </summary>
    public const float MinRampWidth = 1.2f;

    /// <summary>Bake max slope is 45°; 40 leaves margin for the slab thickness.</summary>
    public const float MaxRampAngle = 40f;

    /// <summary>Agent height is 2.0 — anything lower is a ceiling bots refuse.</summary>
    public const float MinHeadroom = 2.2f;

    readonly Transform _root;
    readonly string _arenaName;

    public ArenaKit(Transform root, string arenaName)
    {
        _root = root;
        _arenaName = arenaName;
    }

    // ---------- solids ----------

    /// <summary>A collidable box, centred on <paramref name="center"/>.</summary>
    public GameObject Box(string name, Vector3 center, Vector3 size, Material mat, float yaw = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_root, false);
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>A box with no collider — trim, glow strips, decoration.</summary>
    public GameObject Decor(string name, Vector3 center, Vector3 size, Material mat, float yaw = 0f)
    {
        var go = Box(name, center, size, mat, yaw);
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    public GameObject Cylinder(string name, Vector3 baseCenter, float radius, float height,
                               Material mat, bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(_root, false);
        // Unity's cylinder primitive is 2 units tall, so localScale.y is half-height.
        go.transform.position = baseCenter + Vector3.up * (height * 0.5f);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        // The primitive ships a CapsuleCollider whose rounded caps sit well
        // inside the visual near the base (fighters waded half into fat
        // trunks before a wall ray hit anything) and whose dome top reads
        // as floor. A convex mesh collider IS the cylinder.
        Object.Destroy(go.GetComponent<Collider>());
        if (collide)
        {
            go.AddComponent<MeshCollider>().convex = true;
            // Only a TALL cylinder is a column. A squat one is a dais, a
            // tier, a plinth — something built to stand on — and marking
            // those "never floor" would leave robots unable to walk up
            // their own reactor.
            if (height > radius * 2.2f)
                go.AddComponent<ArenaColumn>();
        }
        return go;
    }

    /// <summary>
    /// An oriented box spanning two arbitrary points — fronds, roots, leaning
    /// spires, pipes. Ramp() refuses steep angles because bots have to walk on
    /// it; this is the same shape with no such opinion, for things nobody walks
    /// on.
    /// </summary>
    public GameObject Beam(string name, Vector3 from, Vector3 to, float thickness,
                           Material mat, bool collide = true)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.01f)
        {
            Debug.LogWarning($"[{_arenaName}] Beam '{name}' has zero length — skipped.");
            return null;
        }

        Vector3 dir = delta / length;
        // LookRotation degenerates when the direction is vertical, which is
        // exactly what a standing stalk is.
        Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_root, false);
        go.transform.position = (from + to) * 0.5f;
        go.transform.rotation = Quaternion.LookRotation(dir, up);
        go.transform.localScale = new Vector3(thickness, thickness, length);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collide)
            Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    public GameObject Sphere(string name, Vector3 center, float radius, Material mat, bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(_root, false);
        go.transform.position = center;
        go.transform.localScale = Vector3.one * (radius * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collide)
            Object.Destroy(go.GetComponent<Collider>());
        else
            go.AddComponent<ArenaColumn>();   // nobody stands on a boulder's crown
        return go;
    }

    /// <summary>A walkable slab. <paramref name="topY"/> is the surface bots stand on.</summary>
    public GameObject Platform(string name, Vector2 center, Vector2 size, float topY,
                               Material mat, float thickness = 0.4f, float yaw = 0f)
    {
        return Box(name, new Vector3(center.x, topY - thickness * 0.5f, center.y),
                   new Vector3(size.x, thickness, size.y), mat, yaw);
    }

    // ---------- connections between levels ----------

    /// <summary>
    /// A sloped walkway from <paramref name="bottom"/> to <paramref name="top"/>
    /// (both are surface points, not centres). Warns rather than silently
    /// emitting geometry bots cannot use.
    /// </summary>
    public GameObject Ramp(string name, Vector3 bottom, Vector3 top, float width, Material mat,
                           float thickness = 0.3f)
    {
        Vector3 delta = top - bottom;
        var flat = new Vector3(delta.x, 0f, delta.z);
        float run = flat.magnitude;
        float rise = delta.y;

        if (run < 0.01f)
        {
            Debug.LogWarning($"[{_arenaName}] Ramp '{name}' has no horizontal run — skipped.");
            return null;
        }

        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        if (angle > MaxRampAngle)
        {
            Debug.LogWarning($"[{_arenaName}] Ramp '{name}' is {angle:F0}° " +
                             $"(max {MaxRampAngle}°) — bots will refuse to climb it. " +
                             "Lengthen its run or lower its top.");
        }
        if (width < MinRampWidth)
        {
            Debug.LogWarning($"[{_arenaName}] Ramp '{name}' was {width:F2} m wide — " +
                             $"widened to the {MinRampWidth} m the NavMesh needs.");
            width = MinRampWidth;
        }

        float length = Mathf.Sqrt(run * run + rise * rise);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_root, false);
        go.transform.position = (bottom + top) * 0.5f - Vector3.up * (thickness * 0.5f);
        // Face along the travel direction, then pitch nose-up by the slope.
        go.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up)
                                * Quaternion.Euler(-angle, 0f, 0f);
        go.transform.localScale = new Vector3(width, thickness, length);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>
    /// A flight of steps. Rise per step is capped at the CharacterController's
    /// step offset, so the player can actually walk up what the bots path over.
    /// </summary>
    public void Stair(string name, Vector3 bottom, Vector3 top, float width, Material mat,
                      float maxRisePerStep = 0.3f)
    {
        Vector3 delta = top - bottom;
        var flat = new Vector3(delta.x, 0f, delta.z);
        float run = flat.magnitude;
        if (run < 0.01f || delta.y <= 0f)
        {
            Debug.LogWarning($"[{_arenaName}] Stair '{name}' is degenerate — skipped.");
            return;
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.y / maxRisePerStep));
        float rise = delta.y / steps;
        float tread = run / steps;
        Vector3 dir = flat.normalized;
        float yaw = Quaternion.LookRotation(dir, Vector3.up).eulerAngles.y;

        for (int i = 0; i < steps; i++)
        {
            // Each step is a solid block up from the floor, so there is no gap
            // underneath for a character to fall into.
            float stepTop = bottom.y + rise * (i + 1);
            Vector3 centre = bottom + dir * (tread * (i + 0.5f));
            Box($"{name}_{i}", new Vector3(centre.x, stepTop - (stepTop - bottom.y) * 0.5f, centre.z),
                new Vector3(width, stepTop - bottom.y, tread), mat, yaw);
        }
    }

    /// <summary>
    /// Height a robot is willing to leap UP. Below this a link works both ways;
    /// above it, it is a drop only — robots jump down off a catwalk, they do not
    /// spring three metres onto one.
    /// </summary>
    public const float MaxLeapUp = 1.8f;

    /// <summary>
    /// A gap bots will jump rather than walk around, so an upper floor is not a
    /// dead end they path away from. RobotJump turns the crossing into an arc.
    ///
    /// Direction is decided by the drop: short steps are two-way, real drops are
    /// one-way down.
    /// </summary>
    public void Link(Vector3 from, Vector3 to, float width = 1.5f)
    {
        Link(from, to, width, (from.y - to.y) <= MaxLeapUp);
    }

    public void Link(Vector3 from, Vector3 to, float width, bool bidirectional)
    {
        var go = new GameObject("NavLink");
        go.transform.SetParent(_root, false);
        go.transform.position = from;
        var link = go.AddComponent<NavMeshLink>();
        link.startPoint = Vector3.zero;
        link.endPoint = go.transform.InverseTransformPoint(to);
        link.width = width;
        link.bidirectional = bidirectional;
        // Every link sits on the "Jump" area so that forms which cannot jump —
        // tanks — are routed around it by the pathfinder instead of committing
        // to a gap they cannot cross. See RobotJump.JumpArea.
        link.area = RobotJump.JumpArea;
    }

    // ---------- lighting ----------

    public Light Point(Vector3 position, Color color, float intensity = 2.5f, float range = 14f)
    {
        var go = new GameObject("ArenaLight");
        go.transform.SetParent(_root, false);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        return light;
    }

    /// <summary>
    /// A non-attenuating coloured fill. Point lights cannot reach mid-arena from
    /// a corner without blowing that corner out, so the neon rim on robots comes
    /// from these.
    /// </summary>
    public Light Directional(string name, Color color, float intensity, Vector3 euler,
                             bool castShadows = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        return light;
    }
}
