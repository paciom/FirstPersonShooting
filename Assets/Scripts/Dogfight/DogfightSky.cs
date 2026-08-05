using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DOGFIGHT's set: the navy void the transformation clips were shot on, made a
/// place — an airport deck far below (runways, taxi lights, a terminal
/// district wearing Commander's building models), a handful of rock spires and
/// cloud puffs for the speed to read against, two suns, a starfield, and the
/// fly volume.
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

    /// <summary>The airfield: two parallel runways flanking the fight's
    /// centre, so the launch pads sit between them like an apron.</summary>
    const float RunwayLength = 560f;
    const float RunwayWidth = 36f;
    static readonly float[] RunwayX = { -70f, 70f };

    /// <summary>The void's own colour — the camera clears to it, the fog fades
    /// to it, and it is deliberately the navy the transformation clips were
    /// shot on.</summary>
    public static readonly Color SkyTint = new Color(0.016f, 0.035f, 0.09f);
    static readonly Color GroundColor = new Color(0.05f, 0.065f, 0.095f);
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
        BuildTerminalDistrict();
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

    /// <summary>
    /// The deck far below the fight is a night airport: dark tarmac, two
    /// runways with painted markings, and rows of edge/threshold/taxi lights.
    /// It exists to make altitude and speed legible — a void with no texture
    /// under it reads as standing still at any speed — and an airfield does
    /// that with more conviction than a grid.
    ///
    /// The paint and the lights are each ONE mesh (the starfield's trick):
    /// hundreds of quads, two draw calls, vertex colour carrying the
    /// white/green/red/blue language real airfields use.
    /// </summary>
    void BuildGround()
    {
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "Ground";
        slab.transform.SetParent(transform, false);
        slab.transform.localScale = new Vector3(GroundSize, 1f, GroundSize);
        slab.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        slab.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-ground", GroundColor, 0.25f);

        // Runway slabs, a shade lighter than the tarmac around them.
        var runwayMaterial = ArenaMaterials.Lit("dogfight-runway",
            new Color(0.10f, 0.115f, 0.145f), 0.35f);
        foreach (float x in RunwayX)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(strip.GetComponent<Collider>());
            strip.name = "Runway";
            strip.transform.SetParent(transform, false);
            strip.transform.localScale = new Vector3(RunwayWidth, 0.16f, RunwayLength);
            strip.transform.localPosition = new Vector3(x, 0f, 0f);
            strip.GetComponent<MeshRenderer>().sharedMaterial = runwayMaterial;
        }

        // The apron the terminal district stands on, west of runway one.
        var apron = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(apron.GetComponent<Collider>());
        apron.name = "Apron";
        apron.transform.SetParent(transform, false);
        apron.transform.localScale = new Vector3(95f, 0.14f, 280f);
        apron.transform.localPosition = new Vector3(-155f, 0f, 0f);
        apron.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-apron", new Color(0.075f, 0.09f, 0.12f), 0.3f);

        BuildAirfieldPaint();
        BuildAirfieldLights();
    }

    /// <summary>One additive quad per vertex-coloured element, one mesh. The
    /// hard-edged default texture is the paint; the glow sprite is the lamps.</summary>
    GameObject FlatQuadMesh(string name, Texture2D texture, List<Vector3> vertices,
        List<Vector2> uvs, List<Color> colors, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial =
            VfxUtil.MakeAdditiveMaterial(texture, Color.white, 1f);
        return go;
    }

    static void AddFlatQuad(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors,
        List<int> triangles, float x, float z, float width, float length, Color color, float y)
    {
        int v = vertices.Count;
        vertices.Add(new Vector3(x - width * 0.5f, y, z - length * 0.5f));
        vertices.Add(new Vector3(x - width * 0.5f, y, z + length * 0.5f));
        vertices.Add(new Vector3(x + width * 0.5f, y, z + length * 0.5f));
        vertices.Add(new Vector3(x + width * 0.5f, y, z - length * 0.5f));
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        color.a = 1f;
        for (int i = 0; i < 4; i++)
            colors.Add(color);
        triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
        triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
    }

    /// <summary>Runway markings: centreline dashes, edge lines, threshold
    /// piano keys — plus the cyan taxi lines that tie runways to aprons,
    /// keeping a thread of the old grid's glow language on the deck.</summary>
    void BuildAirfieldPaint()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        // Paint sits above the runway slab's top face (0.08); lights higher
        // still. ZWrite is off on the additive shader, so the separations are
        // what stand between these layers and a z-fight.
        const float paintY = 0.15f;
        Color paint = new Color(0.85f, 0.9f, 1f) * 0.5f;
        Color taxi = GridGlow * 0.9f;

        void Quad(float x, float z, float w, float l, Color c) =>
            AddFlatQuad(vertices, uvs, colors, triangles, x, z, w, l, c, paintY);

        foreach (float x in RunwayX)
        {
            // Centreline dashes.
            for (float z = -240f; z <= 240f; z += 24f)
                Quad(x, z, 1.1f, 12f, paint * 1.2f);
            // Continuous edge lines.
            Quad(x - RunwayWidth * 0.5f + 0.8f, 0f, 0.8f, RunwayLength - 16f, paint);
            Quad(x + RunwayWidth * 0.5f - 0.8f, 0f, 0.8f, RunwayLength - 16f, paint);
            // Threshold piano keys at both ends.
            foreach (float end in new[] { -1f, 1f })
                for (int i = 0; i < 8; i++)
                    Quad(x - 14f + i * 4f, end * 262f, 2f, 14f, paint);
        }

        // Taxi lines: an apron lane, its connectors to runway one, crossings
        // between the runways, and a spur to the east hangars.
        Quad(-105f, 0f, 0.9f, 320f, taxi);
        foreach (float z in new[] { -160f, 0f, 160f })
            Quad(-78.5f, z, 53f, 0.9f, taxi);
        foreach (float z in new[] { -220f, 220f })
            Quad(0f, z, 104f, 0.9f, taxi);
        Quad(129f, 0f, 82f, 0.9f, taxi);

        FlatQuadMesh("AirfieldPaint", null, vertices, uvs, colors, triangles);
    }

    /// <summary>The lamps, in the real language: white runway edges, green
    /// thresholds, red ends, blue taxiway edges.</summary>
    void BuildAirfieldLights()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        const float lightY = 0.2f;
        // Intensities stay under the bloom whiteout; these are lamps seen
        // from altitude, not flares.
        Color edge = new Color(1f, 0.95f, 0.8f) * 1.6f;
        Color threshold = new Color(0.25f, 1f, 0.5f) * 1.7f;
        Color stop = new Color(1f, 0.3f, 0.25f) * 1.7f;
        Color taxiBlue = new Color(0.35f, 0.55f, 1f) * 1.5f;

        void Lamp(float x, float z, Color c, float size = 1.3f) =>
            AddFlatQuad(vertices, uvs, colors, triangles, x, z, size, size, c, lightY);

        foreach (float x in RunwayX)
        {
            for (float z = -270f; z <= 270f; z += 20f)
            {
                Lamp(x - RunwayWidth * 0.5f, z, edge);
                Lamp(x + RunwayWidth * 0.5f, z, edge);
            }
            foreach (float end in new[] { -1f, 1f })
                for (int i = 0; i < 12; i++)
                {
                    float across = x - 16.5f + i * 3f;
                    Lamp(across, end * 286f, threshold);
                    Lamp(across, end * 274f, stop, 1.1f);
                }
        }

        // Blue edge lights straddling the apron taxi lane.
        for (float z = -156f; z <= 156f; z += 32f)
        {
            Lamp(-108f, z, taxiBlue, 1.1f);
            Lamp(-102f, z, taxiBlue, 1.1f);
        }

        FlatQuadMesh("AirfieldLights", VfxUtil.GlowTexture, vertices, uvs, colors, triangles);
    }

    /// <summary>
    /// The buildings, borrowed straight from Commander's Resources/Buildings
    /// GLBs: the command center moonlights as the control tower, the factory
    /// as the terminal hall, refineries as hangars. Everything stands outside
    /// the flight fence (<see cref="Radius"/>), so nothing here needs a slot in
    /// <see cref="KeepOffProps"/> — a jet close enough to clip a hangar is
    /// already being turned around by the sky.
    /// </summary>
    void BuildTerminalDistrict()
    {
        // West side: the terminal district on the apron, facing the runways.
        float towerTop = PlaceBuilding("command", -175f, 20f, 90f, 14f, 24f);
        PlaceBuilding("factory", -190f, -55f, 90f, 26f, 11f);
        PlaceBuilding("tech", -175f, -105f, 90f, 16f, 9f);
        PlaceBuilding("refinery", -185f, 85f, 90f, 22f, 10f);
        PlaceBuilding("power", -195f, 140f, 90f, 14f, 9f);
        PlaceBuilding("turret", -142f, -60f, 90f, 3f, 5f);
        PlaceBuilding("turret", -142f, 95f, 90f, 3f, 5f);

        // East side: a pair of outlying hangars so the field reads from every
        // camera heading.
        PlaceBuilding("refinery", 185f, -70f, -90f, 22f, 10f);
        PlaceBuilding("power", 190f, 40f, -90f, 13f, 8f);

        // The obstruction beacon on the tower — the one red light every
        // airport keeps lit.
        if (towerTop > 0f)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(beacon.GetComponent<Collider>());
            beacon.name = "TowerBeacon";
            beacon.transform.SetParent(transform, false);
            beacon.transform.localScale = Vector3.one * 0.8f;
            beacon.transform.localPosition = new Vector3(-175f, towerTop + 0.5f, 20f);
            beacon.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Emissive("dogfight-beacon", new Color(1f, 0.3f, 0.25f), 1.8f);
        }
    }

    /// <summary>
    /// Instantiate one Commander building GLB and fit it to a footprint —
    /// Building.BuildFromMeshyModel's sizing rules (uniform scale to the
    /// tightest axis, multiplied into the prefab's own glTF unit scale, feet
    /// on the ground) without the shield, collider or NavMesh carve, because
    /// here they are scenery. Returns the fitted height, 0 if the model is
    /// missing — set dressing must never break the mode.
    /// </summary>
    float PlaceBuilding(string key, float x, float z, float yaw,
        float footprint, float height)
    {
        var prefab = Resources.Load<GameObject>($"Buildings/{key}-building");
        if (prefab == null)
            return 0f;

        var holder = new GameObject($"Airport_{key}");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(x, 0.08f, z);

        var instance = Instantiate(prefab, holder.transform);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        float top = 0f;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            float scale = Mathf.Min(
                footprint / Mathf.Max(0.01f, bounds.size.x),
                height / Mathf.Max(0.01f, bounds.size.y),
                footprint / Mathf.Max(0.01f, bounds.size.z));
            instance.transform.localScale *= scale;

            Vector3 localCenter = holder.transform.InverseTransformPoint(bounds.center);
            Vector3 localBottom = holder.transform.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            instance.transform.localPosition = new Vector3(
                -localCenter.x * scale, -localBottom.y * scale, -localCenter.z * scale);
            top = bounds.size.y * scale;
        }

        // Rotate last — the fit above measured the unrotated world AABB.
        holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return top;
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
