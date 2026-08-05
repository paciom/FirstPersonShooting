using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DOGFIGHT's set: the navy void the transformation clips were shot on, made a
/// place — a glowing ground grid far below, a handful of rock spires and cloud
/// puffs for the speed to read against, two suns, a starfield, and the fly
/// volume.
///
/// THE VOLUME IS SOFT. <see cref="SteerAssist"/> leans on the same steer values
/// the drivers write, ramping in over the last stretch before an edge, so the
/// sky turns a jet around instead of stopping it — a wall you can hit is a wall
/// a kid will hit all day. The player and the AI go through the identical
/// assist, which is what keeps "the AI never leaves the arena" from being a
/// separate piece of cleverness in the brain.
///
/// Constants are expressed against each other: the spawn ring sits inside the
/// turn-back band, the spires stop below the fight floor's approach, the
/// camera's far plane covers the ground's corners. Change one, the rest follow.
/// </summary>
public class DogfightSky : MonoBehaviour
{
    /// <summary>Metres from centre the fight may roam before the sky starts
    /// steering it home.</summary>
    public const float Radius = 150f;

    /// <summary>The soft floor and ceiling of the fight.</summary>
    public const float FloorY = 16f;
    public const float CeilingY = 110f;

    /// <summary>Metres before an edge the turn-back assist reaches full
    /// strength. A jet at boost covers it in about two seconds — time to feel
    /// the nudge before it becomes an argument.</summary>
    const float AssistBand = 80f;

    /// <summary>Where jets are (re)born: well inside the turn-back band, at an
    /// altitude with room both ways.</summary>
    public const float SpawnRing = Radius - AssistBand * 0.9f;
    public const float SpawnAltitude = (FloorY + CeilingY) * 0.36f;

    /// <summary>Where the intro pads stand. Above the soft floor on purpose:
    /// the robots transform, throttle up and CLIMB into the volume.</summary>
    public const float PadY = 12f;

    const float GroundSize = 640f;

    /// <summary>The void's own colour — the camera clears to it, the fog fades
    /// to it, and it is deliberately the navy the transformation clips were
    /// shot on.</summary>
    public static readonly Color SkyTint = new Color(0.016f, 0.035f, 0.09f);
    static readonly Color GroundColor = new Color(0.05f, 0.10f, 0.16f);
    static readonly Color GridGlow = new Color(0.15f, 0.75f, 0.85f);

    static readonly List<Transform> Spires = new List<Transform>();
    static readonly List<float> SpireRadii = new List<float>();

    readonly List<Transform> _clouds = new List<Transform>();

    public static DogfightSky Build(Transform stageRoot)
    {
        var go = new GameObject("DogfightSky");
        go.transform.SetParent(stageRoot, false);
        var sky = go.AddComponent<DogfightSky>();
        sky.Compose();
        return sky;
    }

    void Compose()
    {
        Spires.Clear();
        SpireRadii.Clear();

        BuildGround();
        BuildSpires();
        BuildClouds();
        BuildStars();

        Sun("KeySun", new Vector3(48f, 42f, 0f), 1.05f, new Color(1f, 0.97f, 0.9f));
        Sun("FillSun", new Vector3(18f, 218f, 0f), 0.35f, new Color(0.5f, 0.7f, 1f));

        // The arena's atmosphere is asleep under this mode; hand the renderer a
        // sky of our own. All of it is re-applied by ArenaRuntime.Load on the
        // way out — the same contract Commander leans on when it recolours the
        // world.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.42f, 0.58f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = SkyTint;
        RenderSettings.fogDensity = 0.0028f;
    }

    /// <summary>The deck far below the fight: dark slab, glowing grid lines. It
    /// exists to make altitude and speed legible — a void with no texture under
    /// it reads as standing still at any speed.</summary>
    void BuildGround()
    {
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "Ground";
        slab.transform.SetParent(transform, false);
        slab.transform.localScale = new Vector3(GroundSize, 1f, GroundSize);
        slab.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        slab.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-ground", GroundColor, 0.25f);

        var lineMaterial = ArenaMaterials.Emissive("dogfight-grid", GridGlow, 1.5f);
        const int lines = 9;
        const float pitch = GroundSize / (lines + 1);
        for (int axis = 0; axis < 2; axis++)
            for (int i = 0; i < lines; i++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(line.GetComponent<Collider>());
                line.name = "GridLine";
                line.transform.SetParent(transform, false);
                float offset = (i - (lines - 1) * 0.5f) * pitch;
                line.transform.localPosition = axis == 0
                    ? new Vector3(offset, 0.08f, 0f)
                    : new Vector3(0f, 0.08f, offset);
                line.transform.localScale = axis == 0
                    ? new Vector3(0.9f, 0.12f, GroundSize)
                    : new Vector3(GroundSize, 0.12f, 0.9f);
                line.GetComponent<MeshRenderer>().sharedMaterial = lineMaterial;
            }
    }

    /// <summary>
    /// Rock spires between the deck and the fight floor. They are what near
    /// misses happen against; their tops stay a jet-length below
    /// <see cref="FloorY"/> plus the dive the assist band tolerates, so only a
    /// jet that is already being turned around can brush one.
    /// </summary>
    void BuildSpires()
    {
        var rock = ArenaMaterials.Lit("dogfight-spire", new Color(0.10f, 0.14f, 0.22f), 0.3f);
        var glow = ArenaMaterials.Emissive("dogfight-spire-band", GridGlow, 1.2f);
        // Deterministic ring: this is set dressing, not level generation, and a
        // fixed seed means a screenshot names the build.
        var random = new System.Random(77);
        const int count = 9;
        for (int i = 0; i < count; i++)
        {
            float azimuth = (i + 0.5f) * (360f / count) + (float)random.NextDouble() * 18f;
            float distance = Mathf.Lerp(Radius * 0.25f, Radius * 0.85f,
                (float)random.NextDouble());
            float height = Mathf.Lerp(4f, FloorY * 0.65f, (float)random.NextDouble());
            float width = Mathf.Lerp(3f, 7f, (float)random.NextDouble());
            Vector3 at = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward * distance;

            var spire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spire.name = "Spire";
            spire.transform.SetParent(transform, false);
            // Cylinder primitive is 2 units tall at scale 1.
            spire.transform.localScale = new Vector3(width, height * 0.5f, width);
            spire.transform.localPosition = at + Vector3.up * (height * 0.5f);
            spire.GetComponent<MeshRenderer>().sharedMaterial = rock;

            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(band.GetComponent<Collider>());
            band.name = "SpireBand";
            band.transform.SetParent(spire.transform, false);
            band.transform.localScale = new Vector3(1.04f, 0.02f, 1.04f);
            band.transform.localPosition = new Vector3(0f, 0.82f, 0f);
            band.GetComponent<MeshRenderer>().sharedMaterial = glow;

            Spires.Add(spire.transform);
            SpireRadii.Add(width * 0.5f);
        }
    }

    /// <summary>Soft puffs drifting through the fight band. Pure speed cues —
    /// no colliders, faint enough that a jet vanishing into one for a beat is
    /// atmosphere, not occlusion.</summary>
    void BuildClouds()
    {
        var material = VfxUtil.MakeAdditiveMaterial(VfxUtil.PuffTexture,
            new Color(0.65f, 0.75f, 0.95f), 0.28f);
        var random = new System.Random(41);
        for (int i = 0; i < 14; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.name = "Cloud";
            quad.transform.SetParent(transform, false);
            float azimuth = (float)random.NextDouble() * 360f;
            float distance = Mathf.Lerp(30f, Radius * 1.1f, (float)random.NextDouble());
            float altitude = Mathf.Lerp(FloorY + 8f, CeilingY - 12f, (float)random.NextDouble());
            quad.transform.localPosition =
                Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward * distance
                + Vector3.up * altitude;
            float size = Mathf.Lerp(14f, 34f, (float)random.NextDouble());
            quad.transform.localScale = new Vector3(size, size * 0.55f, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            _clouds.Add(quad.transform);
        }
    }

    /// <summary>
    /// The stars: one mesh, one draw call. Each star is a small glow-sprite
    /// quad pinned to a dome inside the camera's far plane, facing the arena's
    /// centre — the camera never strays far enough from centre for the flat
    /// facing to read as anything but a point. Brightness and tint live in
    /// vertex colour; the additive shader skips fog, which is what lets them
    /// survive a fog density that swallows lit geometry long before the dome.
    /// </summary>
    void BuildStars()
    {
        // Far plane is 900 and the camera roams ~170 from centre at worst;
        // 680 keeps every star inside the clip with margin.
        const float dome = 680f;
        const int count = 520;
        var random = new System.Random(23);

        var vertices = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var colors = new Color[count * 4];
        var triangles = new int[count * 6];

        // A tilted band gets a denser share of the stars — a cheap milky way.
        Quaternion bandTilt = Quaternion.Euler(62f, 0f, 24f);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir;
            if (i % 5 < 2)
            {
                // Band star: along a tilted great circle, scattered off it.
                float along = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 onCircle = new Vector3(Mathf.Cos(along), 0f, Mathf.Sin(along));
                Vector3 jitter = new Vector3(
                    (float)random.NextDouble() - 0.5f,
                    (float)random.NextDouble() - 0.5f,
                    (float)random.NextDouble() - 0.5f) * 0.5f;
                dir = (bandTilt * onCircle + jitter).normalized;
            }
            else
            {
                // Uniform over the dome: y uniform is area uniform on a sphere.
                float y = Mathf.Lerp(-0.05f, 1f, (float)random.NextDouble());
                float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
                float flat = Mathf.Sqrt(1f - y * y);
                dir = new Vector3(Mathf.Cos(azimuth) * flat, y, Mathf.Sin(azimuth) * flat);
            }
            if (dir.y < -0.05f)
                dir.y = -0.05f + ((float)random.NextDouble() * 0.3f);
            dir = dir.normalized;

            float roll = (float)random.NextDouble();
            float size = Mathf.Lerp(1.6f, 3.4f, roll * roll);
            float glow = Mathf.Lerp(0.35f, 1.1f, (float)random.NextDouble());
            Color tint = Color.white;
            float hue = (float)random.NextDouble();
            if (hue < 0.18f)
                tint = new Color(0.65f, 0.8f, 1f);
            else if (hue < 0.28f)
                tint = new Color(1f, 0.9f, 0.75f);
            if (roll > 0.96f)
            {
                // A handful of standouts — bright but under the bloom whiteout.
                size *= 1.8f;
                glow = 1.7f;
            }

            Vector3 position = dir * dome;
            Vector3 axis = Mathf.Abs(dir.y) > 0.98f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(axis, dir).normalized * size;
            Vector3 up = Vector3.Cross(dir, right).normalized * size;

            int v = i * 4;
            vertices[v] = position - right - up;
            vertices[v + 1] = position - right + up;
            vertices[v + 2] = position + right + up;
            vertices[v + 3] = position + right - up;
            uvs[v] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(0f, 1f);
            uvs[v + 2] = new Vector2(1f, 1f);
            uvs[v + 3] = new Vector2(1f, 0f);
            Color c = tint * glow;
            c.a = 1f;
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "DogfightStars" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;

        var go = new GameObject("Stars");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial =
            VfxUtil.MakeAdditiveMaterial(VfxUtil.GlowTexture, Color.white, 1f);
    }

    /// <summary>The intro pad one robot stands on. Handed back so the mode can
    /// burst and sink it at launch.</summary>
    public GameObject BuildPad(Vector3 position, Color teamColor)
    {
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "LaunchPad";
        pad.transform.SetParent(transform, false);
        pad.transform.localScale = new Vector3(6f, 0.35f, 6f);
        pad.transform.localPosition = position - Vector3.up * 0.35f;
        pad.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-pad", new Color(0.09f, 0.13f, 0.2f), 0.5f);

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(ring.GetComponent<Collider>());
        ring.name = "PadRing";
        ring.transform.SetParent(pad.transform, false);
        ring.transform.localScale = new Vector3(1.05f, 0.1f, 1.05f);
        // Proud of the pad's top face, never level with it: two coplanar
        // cylinder caps z-fight as a wheel of flashing triangles.
        ring.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Emissive(
            $"dogfight-pad-{ColorUtility.ToHtmlStringRGB(teamColor)}", teamColor, 1.8f);
        return pad;
    }

    void Update()
    {
        // Clouds face whoever is looking. Yaw only: a puff that pitches over to
        // meet a diving camera visibly lies down.
        var camera = Camera.main;
        if (camera == null)
            return;
        foreach (var cloud in _clouds)
        {
            if (cloud == null)
                continue;
            Vector3 to = camera.transform.position - cloud.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-4f)
                cloud.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
        }
    }

    void OnDestroy()
    {
        Spires.Clear();
        SpireRadii.Clear();
    }

    void Sun(string name, Vector3 euler, float intensity, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
    }

    // --------------------------------------------------------------- the volume

    /// <summary>
    /// The sky's hand on the stick: blends the driver's steer toward "back
    /// inside" as a jet nears the edge, the floor or the ceiling, reaching full
    /// authority at the boundary itself. Centripetal rather than a hard flip —
    /// the yaw component pushes toward the centre's side, so an edge approach
    /// becomes a wide banked turn back into the fight.
    /// </summary>
    public static Vector2 SteerAssist(Vector3 position, Vector3 forward, Vector2 steer)
    {
        // Turn back from the fence.
        Vector3 flatPos = new Vector3(position.x, 0f, position.z);
        float outside = flatPos.magnitude - (Radius - AssistBand);
        if (outside > 0f && flatPos.sqrMagnitude > 1e-4f)
        {
            float grip = Mathf.Clamp01(outside / AssistBand);
            Vector3 home = -flatPos.normalized;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
            float toward = Vector3.Dot(flatForward, home);
            if (toward < 0.85f)
            {
                float side = Mathf.Sign(Vector3.SignedAngle(flatForward, home, Vector3.up));
                steer.x = Mathf.Lerp(steer.x, side, grip * grip);
            }
        }

        // Pull up from the floor, push down from the ceiling. The bands are
        // shallower than the fence's: altitude mistakes happen faster.
        float lift = Mathf.Clamp01((FloorY + 22f - position.y) / 22f);
        if (lift > 0f)
            steer.y = Mathf.Lerp(steer.y, 1f, lift * lift);
        float duck = Mathf.Clamp01((position.y - (CeilingY - 22f)) / 22f);
        if (duck > 0f)
            steer.y = Mathf.Lerp(steer.y, -1f, duck * duck);

        return steer;
    }

    /// <summary>
    /// Keep jets out of the spires. A positional push, TankPawn's
    /// AvoidScenery reasoning in the air: everything here moves by writing its
    /// own transform, so Unity's collision response never runs, and cover you
    /// can be shot around but flown through is a rule nobody can learn.
    /// </summary>
    public static Vector3 KeepOffProps(Vector3 position, float clearance)
    {
        for (int i = 0; i < Spires.Count; i++)
        {
            var spire = Spires[i];
            if (spire == null)
                continue;
            // A cylinder test, because a spire is one: horizontal push only,
            // and only below its cap (localScale.y is its half-height).
            float top = spire.position.y + spire.localScale.y;
            if (position.y > top + clearance)
                continue;
            Vector3 gap = position - spire.position;
            gap.y = 0f;
            float want = SpireRadii[i] + clearance;
            float distance = gap.magnitude;
            if (distance >= want || distance < 1e-4f)
                continue;
            Vector3 pushed = spire.position + gap / distance * want;
            position = new Vector3(pushed.x, position.y, pushed.z);
        }
        return position;
    }
}
