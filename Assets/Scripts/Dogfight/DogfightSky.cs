using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DOGFIGHT's set, in three flavours picked on the select screen
/// (<see cref="DogfightMapPick"/>):
///
///  - AIRFIELD — the original navy void made a place: a night airport far
///    below, rock spires, cloud puffs, two suns and a starfield.
///  - TINY PLANET — a 130 m worldlet whose north pole is the deck. The
///    horizon curves away in every direction, a tilted ring and a slow moon
///    hang beyond the fence, and the soft floor follows the SURFACE, so the
///    fight swoops around the dome instead of hovering over a plane.
///  - DONUT STATION — a torus station floating in open space: launch pads on
///    the hub deck, a ring walkway the turrets stand on, spokes between, and
///    the hole through the middle begging to be flown through. Below the
///    furniture there is only space — no deck, no gravity; what drifts off,
///    drifts (see <see cref="InOpenSpace"/>).
///
/// THE VOLUME IS SOFT. <see cref="SteerAssist"/> leans on the same steer values
/// the drivers write, ramping in over the last stretch before an edge, so the
/// sky turns a jet around instead of stopping it — a wall you can hit is a wall
/// a kid will hit all day. The player and the AI go through the identical
/// assist, which is what keeps "the AI never leaves the arena" from being a
/// separate piece of cleverness in the brain.
///
/// THE DECK IS A QUERY, NOT A CONSTANT. Every landing, wreck crash and ground
/// gait asks <see cref="GroundHeight"/> what is underfoot, which is what lets
/// one map curve the ground and another float it in pieces over nothing.
/// Constants are expressed against each other: the spawn ring sits inside the
/// turn-back band, the props stop below the fight floor's approach, the
/// camera's far plane covers the furthest dressing. Change one, the rest
/// follow.
/// </summary>
public class DogfightSky : MonoBehaviour
{
    /// <summary>Metres from centre the fight may roam before the sky starts
    /// steering it home.</summary>
    public const float Radius = 150f;

    /// <summary>The soft floor and ceiling of the fight. The floor is the
    /// AIRFIELD's flat one; the other maps answer through
    /// <see cref="LocalFloor"/>.</summary>
    public const float FloorY = 16f;
    public const float CeilingY = 110f;

    /// <summary>Metres before an edge the turn-back assist reaches full
    /// strength. A jet at boost covers it in about two seconds — time to feel
    /// the nudge before it becomes an argument.</summary>
    const float AssistBand = 80f;

    /// <summary>Where jets are (re)born: well inside the turn-back band, at an
    /// altitude with room both ways.</summary>
    public const float SpawnRing = Radius - AssistBand * 0.9f;

    /// <summary>The battlefield being flown. Cached from the pick when the set
    /// is built; a mid-Play recompile wipes the cache and the next read falls
    /// back to the store, which cannot have changed mid-sortie.</summary>
    static DogfightMapKind? _map;
    public static DogfightMapKind Map => _map ??= DogfightMapPick.Chosen;

    /// <summary>Respawn altitude. The station map lifts it clear of the ring
    /// walkway — the default ring altitude would set jets down ON the deck.</summary>
    public static float SpawnAltitude =>
        Map == DogfightMapKind.DonutStation ? 74f : (FloorY + CeilingY) * 0.36f;

    /// <summary>Where the intro pads stand: always a floating platform a
    /// climb's worth above whatever counts as the deck beneath them.</summary>
    public static float PadY =>
        Map == DogfightMapKind.DonutStation ? HubTopY + 12f : 12f;

    // ------------------------------------------------------------- tiny planet

    /// <summary>The worldlet. Its north pole touches y = 0, so pads, turrets
    /// and the fight's numbers all line up with the airfield's.</summary>
    const float PlanetRadius = 130f;
    static readonly Vector3 PlanetCenter = new Vector3(0f, -PlanetRadius, 0f);

    /// <summary>Past this horizontal distance the surface is treated as the
    /// near-vertical flank: the deck query clamps here, which is what keeps a
    /// chute that drifts past the rim landing beside the silhouette instead
    /// of hovering in space.</summary>
    const float PlanetRim = 129f;

    /// <summary>The planet's soft floor rides this far above the LOCAL
    /// surface — the airfield floor rule, bent around a sphere.</summary>
    const float PlanetFloorLift = 14f;

    // ----------------------------------------------------------- donut station

    const float TorusY = 30f;        // the tube's centre plane
    const float TorusMajor = 72f;    // ring spine radius
    const float TorusTube = 14f;     // tube radius

    const float HubRadius = 34f;
    const float HubTopY = 35f;       // the hub deck the pads launch off
    const float HubBottomY = 19f;

    /// <summary>The flat walkway laid over the tube's crown — proud of the
    /// curve so it reads as built architecture, wide enough to fight on.</summary>
    const float RingDeckY = TorusY + TorusTube + 0.2f;
    const float RingDeckInner = 64f;
    const float RingDeckOuter = 80f;

    /// <summary>The deck height REPORTED where the station has no furniture:
    /// a mathematical answer that keeps the deck queries total, not a place.
    /// Nothing is drawn there and nothing lands there — the void is open
    /// space, and fallers in it drift (<see cref="InOpenSpace"/>).</summary>
    const float VoidY = -40f;

    /// <summary>The station map's flat soft floor for JETS: low enough to fly
    /// under the ring, with only stars below it.</summary>
    const float DonutFloorY = 8f;

    const float SpokeGirth = 2.75f;

    // ----------------------------------------------------------------- palette

    /// <summary>The void's own colour — the camera clears to it, the fog fades
    /// to it, and it is deliberately the navy the transformation clips were
    /// shot on.</summary>
    public static readonly Color SkyTint = new Color(0.016f, 0.035f, 0.09f);
    static readonly Color GroundColor = new Color(0.05f, 0.065f, 0.095f);
    static readonly Color GridGlow = new Color(0.15f, 0.75f, 0.85f);

    // ---------------------------------------------------------------- airfield

    const float GroundSize = 640f;

    /// <summary>The airfield: two parallel runways flanking the fight's
    /// centre, so the launch pads sit between them like an apron.</summary>
    const float RunwayLength = 560f;
    const float RunwayWidth = 36f;
    static readonly float[] RunwayX = { -70f, 70f };

    static readonly List<Transform> Spires = new List<Transform>();
    static readonly List<float> SpireRadii = new List<float>();

    readonly List<Transform> _clouds = new List<Transform>();
    Transform _moonPivot;

    public static DogfightSky Build(Transform stageRoot)
    {
        // Re-read the pick for THIS sortie before anything measures the map.
        _map = DogfightMapPick.Chosen;

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

        switch (Map)
        {
            case DogfightMapKind.TinyPlanet:
                BuildPlanet();
                BuildPlanetProps();
                BuildPlanetRing();
                BuildMoon();
                BuildClouds();
                BuildStars(true);
                break;

            case DogfightMapKind.DonutStation:
                BuildTorus();
                BuildHub();
                BuildSpokes();
                BuildRingDeck();
                BuildStationWindows();
                BuildStationLamps();
                BuildStars(true);
                break;

            default:
                BuildGround();
                BuildTerminalDistrict();
                BuildSpires();
                BuildClouds();
                BuildStars(false);
                break;
        }

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

    // ------------------------------------------------------- airfield builders

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
        AddOrientedQuad(vertices, uvs, colors, triangles,
            new Vector3(x, y, z),
            Vector3.right * (width * 0.5f), Vector3.forward * (length * 0.5f), color);
    }

    /// <summary>A quad on any plane: centre plus half-extent basis vectors.
    /// The flat deck paint, the station's windows and the planet's moss all
    /// go through here.</summary>
    static void AddOrientedQuad(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors,
        List<int> triangles, Vector3 center, Vector3 halfRight, Vector3 halfUp, Color color)
    {
        int v = vertices.Count;
        vertices.Add(center - halfRight - halfUp);
        vertices.Add(center - halfRight + halfUp);
        vertices.Add(center + halfRight + halfUp);
        vertices.Add(center + halfRight - halfUp);
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
            at = KeepOffPavement(at, width * 0.5f + 2f);

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

    /// <summary>A spire footprint may not stand on the airfield's paved
    /// rectangles — rock through the runway centreline reads as a mistake,
    /// not scenery. Pushed sideways to the nearer shoulder, which keeps the
    /// ring shape while clearing the pavement.</summary>
    static Vector3 KeepOffPavement(Vector3 at, float clearance)
    {
        foreach (float runwayX in RunwayX)
            at = PushOutX(at, runwayX, RunwayWidth * 0.5f, RunwayLength * 0.5f, clearance);
        // The apron under the terminal district (see BuildGround).
        at = PushOutX(at, -155f, 47.5f, 140f, clearance);
        return at;
    }

    static Vector3 PushOutX(Vector3 at, float centerX, float halfWidth,
        float halfLength, float clearance)
    {
        if (Mathf.Abs(at.z) > halfLength + clearance)
            return at;
        float offset = at.x - centerX;
        float want = halfWidth + clearance;
        if (Mathf.Abs(offset) >= want)
            return at;
        return new Vector3(centerX + Mathf.Sign(offset) * want, at.y, at.z);
    }

    // ---------------------------------------------------- tiny planet builders

    /// <summary>The worldlet itself: one big sphere (its collider is what
    /// bolts and gun runs hit), with a glowing equator band poking out of the
    /// rock the way the pad rings do.</summary>
    void BuildPlanet()
    {
        var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "Planet";
        planet.transform.SetParent(transform, false);
        planet.transform.localScale = Vector3.one * (PlanetRadius * 2f);
        planet.transform.localPosition = PlanetCenter;
        planet.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-planet", new Color(0.11f, 0.10f, 0.16f), 0.25f);

        // The equator band: a disc a shade wider than the sphere, so only its
        // rim shows — a line of light around the worldlet's waist.
        var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(band.GetComponent<Collider>());
        band.name = "EquatorBand";
        band.transform.SetParent(transform, false);
        band.transform.localScale = new Vector3(PlanetRadius * 2f + 3f, 1.1f,
            PlanetRadius * 2f + 3f);
        band.transform.localPosition = PlanetCenter;
        band.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("dogfight-equator", GridGlow, 1.2f);
    }

    /// <summary>
    /// Life on the dome: crystals, boulders and mushroom-trees scattered over
    /// the upper hemisphere, each standing along its own bit of "up", plus
    /// faint moss glows painted onto the rock. Everything stays under the
    /// planet's soft floor (surface + <see cref="PlanetFloorLift"/>), the
    /// spires' rule bent around a sphere — a skimming jet clears the tallest
    /// prop with room to spare.
    /// </summary>
    void BuildPlanetProps()
    {
        var rock = ArenaMaterials.Lit("dogfight-boulder", new Color(0.16f, 0.15f, 0.22f), 0.3f);
        var cap = ArenaMaterials.Lit("dogfight-mushroom", new Color(0.10f, 0.28f, 0.26f), 0.4f);
        var crystalCyan = ArenaMaterials.Emissive("dogfight-crystal-cyan", GridGlow, 1.3f);
        var crystalMagenta = ArenaMaterials.Emissive("dogfight-crystal-magenta",
            new Color(0.85f, 0.3f, 0.9f), 1.3f);

        var mossVerts = new List<Vector3>();
        var mossUvs = new List<Vector2>();
        var mossColors = new List<Color>();
        var mossTris = new List<int>();

        var random = new System.Random(19);
        const int count = 64;
        for (int i = 0; i < count; i++)
        {
            // Polar 12°–52° from the pole: clear of the pad apron at the top,
            // stopped where the walkable dome ends and the flank begins.
            float polar = Mathf.Lerp(12f, 52f, (float)random.NextDouble()) * Mathf.Deg2Rad;
            float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 up = new Vector3(
                Mathf.Sin(polar) * Mathf.Cos(azimuth),
                Mathf.Cos(polar),
                Mathf.Sin(polar) * Mathf.Sin(azimuth));
            Vector3 foot = PlanetCenter + up * PlanetRadius;

            float roll = (float)random.NextDouble();
            if (roll < 0.25f)
            {
                // Moss: a soft glow painted on the rock, no body at all.
                Vector3 right = Vector3.Cross(up, Vector3.forward).normalized;
                Vector3 along = Vector3.Cross(right, up);
                float size = Mathf.Lerp(3f, 7f, (float)random.NextDouble());
                AddOrientedQuad(mossVerts, mossUvs, mossColors, mossTris,
                    foot + up * 0.25f, right * size, along * size,
                    new Color(0.2f, 0.8f, 0.6f) * 0.35f);
                continue;
            }

            var holder = new GameObject("PlanetProp");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = foot;
            holder.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up)
                * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

            if (roll < 0.5f)
            {
                // A crystal, leaning a little off plumb, tip glowing.
                float height = Mathf.Lerp(3f, 8f, (float)random.NextDouble());
                float width = Mathf.Lerp(0.8f, 1.8f, (float)random.NextDouble());
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                crystal.name = "Crystal";
                crystal.transform.SetParent(holder.transform, false);
                crystal.transform.localScale = new Vector3(width, height * 0.5f, width);
                crystal.transform.localRotation = Quaternion.Euler(
                    Mathf.Lerp(-14f, 14f, (float)random.NextDouble()), 0f,
                    Mathf.Lerp(-14f, 14f, (float)random.NextDouble()));
                crystal.transform.localPosition = crystal.transform.localRotation
                    * Vector3.up * (height * 0.5f);
                crystal.GetComponent<MeshRenderer>().sharedMaterial =
                    i % 2 == 0 ? crystalCyan : crystalMagenta;
            }
            else if (roll < 0.78f)
            {
                // A boulder — tumbled, half-buried.
                var boulder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                boulder.name = "Boulder";
                boulder.transform.SetParent(holder.transform, false);
                boulder.transform.localScale = new Vector3(
                    Mathf.Lerp(1.5f, 4f, (float)random.NextDouble()),
                    Mathf.Lerp(1.2f, 3f, (float)random.NextDouble()),
                    Mathf.Lerp(1.5f, 4f, (float)random.NextDouble()));
                boulder.transform.localRotation = Quaternion.Euler(
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f);
                boulder.transform.localPosition = Vector3.up * 0.6f;
                boulder.GetComponent<MeshRenderer>().sharedMaterial = rock;
            }
            else
            {
                // A mushroom-tree: pale trunk, wide teal cap.
                float trunk = Mathf.Lerp(1.5f, 3f, (float)random.NextDouble());
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.name = "Trunk";
                stem.transform.SetParent(holder.transform, false);
                stem.transform.localScale = new Vector3(0.5f, trunk * 0.5f, 0.5f);
                stem.transform.localPosition = Vector3.up * (trunk * 0.5f);
                stem.GetComponent<MeshRenderer>().sharedMaterial = rock;

                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Cap";
                crown.transform.SetParent(holder.transform, false);
                float spread = Mathf.Lerp(2f, 3.6f, (float)random.NextDouble());
                crown.transform.localScale = new Vector3(spread, spread * 0.4f, spread);
                crown.transform.localPosition = Vector3.up * trunk;
                crown.GetComponent<MeshRenderer>().sharedMaterial = cap;
            }
        }

        FlatQuadMesh("PlanetMoss", VfxUtil.GlowTexture,
            mossVerts, mossUvs, mossColors, mossTris);
    }

    /// <summary>The Saturn ring: two translucent bands with a gap, tilted so
    /// the near side climbs into view when a jet swoops the rim. Wholly
    /// outside the fence — dressing, never an obstacle. Vertex colour fades
    /// the bands' edges to nothing (additive: dark IS transparent).</summary>
    void BuildPlanetRing()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        Color tint = new Color(0.45f, 0.6f, 0.9f);
        void Band(float inner, float outer, float brightness)
        {
            const int segments = 72;
            float mid = (inner + outer) * 0.5f;
            int first = vertices.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = s * (Mathf.PI * 2f / segments);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(dir * inner);
                vertices.Add(dir * mid);
                vertices.Add(dir * outer);
                for (int k = 0; k < 3; k++)
                    uvs.Add(new Vector2(0.5f, 0.5f));
                colors.Add(Color.black);
                colors.Add(tint * brightness);
                colors.Add(Color.black);
                if (s == 0)
                    continue;
                int a0 = first + (s - 1) * 3, b0 = first + s * 3;
                for (int k = 0; k < 2; k++)
                {
                    triangles.Add(a0 + k); triangles.Add(a0 + k + 1); triangles.Add(b0 + k + 1);
                    triangles.Add(a0 + k); triangles.Add(b0 + k + 1); triangles.Add(b0 + k);
                }
            }
        }

        Band(175f, 202f, 0.38f);
        Band(210f, 236f, 0.24f);

        var ring = FlatQuadMesh("PlanetRing", null, vertices, uvs, colors, triangles);
        ring.transform.localPosition = PlanetCenter;
        ring.transform.localRotation = Quaternion.Euler(32f, 0f, 6f);
    }

    /// <summary>A pale moon on a lazy orbit past the fence — parallax for the
    /// stars and something to frame a dogfight against.</summary>
    void BuildMoon()
    {
        _moonPivot = new GameObject("MoonOrbit").transform;
        _moonPivot.SetParent(transform, false);

        var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(moon.GetComponent<Collider>());
        moon.name = "Moon";
        moon.transform.SetParent(_moonPivot, false);
        moon.transform.localScale = Vector3.one * 44f;
        moon.transform.localPosition = new Vector3(240f, 88f, 0f);
        moon.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-moon", new Color(0.35f, 0.37f, 0.45f), 0.2f);
    }

    // -------------------------------------------------- donut station builders

    /// <summary>The donut itself: a real torus mesh with a real mesh collider,
    /// so bolts hit it and gun runs use it as cover.</summary>
    void BuildTorus()
    {
        var mesh = TorusMesh(TorusMajor, TorusTube, 56, 20);
        var go = new GameObject("StationRing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, TorusY, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-station", new Color(0.14f, 0.17f, 0.24f), 0.45f);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static Mesh TorusMesh(float majorR, float tubeR, int segments, int sides)
    {
        var vertices = new Vector3[(segments + 1) * (sides + 1)];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[segments * sides * 6];

        for (int s = 0; s <= segments; s++)
        {
            float a = s * (Mathf.PI * 2f / segments);
            Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 spine = outward * majorR;
            for (int t = 0; t <= sides; t++)
            {
                float b = t * (Mathf.PI * 2f / sides);
                Vector3 normal = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                int v = s * (sides + 1) + t;
                vertices[v] = spine + normal * tubeR;
                normals[v] = normal;
                uvs[v] = new Vector2((float)s / segments, (float)t / sides);
            }
        }

        int i = 0;
        for (int s = 0; s < segments; s++)
            for (int t = 0; t < sides; t++)
            {
                int v = s * (sides + 1) + t;
                int next = v + sides + 1;
                triangles[i++] = v; triangles[i++] = next; triangles[i++] = v + 1;
                triangles[i++] = v + 1; triangles[i++] = next; triangles[i++] = next + 1;
            }

        var mesh = new Mesh { name = "StationTorus" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        return mesh;
    }

    /// <summary>The hub the pads launch off: a drum in the donut's hole, a
    /// glowing rim band, and the comms mast hanging under it with the one red
    /// beacon every station keeps lit.</summary>
    void BuildHub()
    {
        var hull = ArenaMaterials.Lit("dogfight-station", new Color(0.14f, 0.17f, 0.24f), 0.45f);

        var drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        drum.name = "Hub";
        drum.transform.SetParent(transform, false);
        drum.transform.localScale = new Vector3(HubRadius * 2f,
            (HubTopY - HubBottomY) * 0.5f, HubRadius * 2f);
        drum.transform.localPosition = new Vector3(0f, (HubTopY + HubBottomY) * 0.5f, 0f);
        drum.GetComponent<MeshRenderer>().sharedMaterial = hull;

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(rim.GetComponent<Collider>());
        rim.name = "HubRim";
        rim.transform.SetParent(transform, false);
        rim.transform.localScale = new Vector3(HubRadius * 2f + 0.8f, 0.35f, HubRadius * 2f + 0.8f);
        rim.transform.localPosition = new Vector3(0f, HubTopY - 2f, 0f);
        rim.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("dogfight-hub-rim", GridGlow, 1.4f);

        var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mast.name = "HubMast";
        mast.transform.SetParent(transform, false);
        mast.transform.localScale = new Vector3(2.4f, 6.5f, 2.4f);
        mast.transform.localPosition = new Vector3(0f, HubBottomY - 6.5f, 0f);
        mast.GetComponent<MeshRenderer>().sharedMaterial = hull;

        var dish = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(dish.GetComponent<Collider>());
        dish.name = "HubDish";
        dish.transform.SetParent(transform, false);
        dish.transform.localScale = new Vector3(7f, 2.2f, 7f);
        dish.transform.localPosition = new Vector3(0f, HubBottomY - 11f, 0f);
        dish.GetComponent<MeshRenderer>().sharedMaterial = hull;

        var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(beacon.GetComponent<Collider>());
        beacon.name = "MastBeacon";
        beacon.transform.SetParent(transform, false);
        beacon.transform.localScale = Vector3.one * 0.9f;
        beacon.transform.localPosition = new Vector3(0f, HubBottomY - 13.5f, 0f);
        beacon.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("dogfight-beacon", new Color(1f, 0.3f, 0.25f), 1.8f);
    }

    /// <summary>Four beams tying the hub to the ring, each wearing a glow
    /// strip so the cross reads from altitude.</summary>
    void BuildSpokes()
    {
        var hull = ArenaMaterials.Lit("dogfight-station", new Color(0.14f, 0.17f, 0.24f), 0.45f);
        var trim = ArenaMaterials.Emissive("dogfight-spoke-trim", GridGlow, 1.2f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
            var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spoke.name = "Spoke";
            spoke.transform.SetParent(transform, false);
            spoke.transform.localScale = new Vector3(5.5f, 4.5f, 28f);
            spoke.transform.localPosition = dir * 46f + Vector3.up * TorusY;
            spoke.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            spoke.GetComponent<MeshRenderer>().sharedMaterial = hull;

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(strip.GetComponent<Collider>());
            strip.name = "SpokeStrip";
            strip.transform.SetParent(spoke.transform, false);
            strip.transform.localScale = new Vector3(0.2f, 0.06f, 0.9f);
            strip.transform.localPosition = new Vector3(0f, 0.53f, 0f);
            strip.GetComponent<MeshRenderer>().sharedMaterial = trim;
        }
    }

    /// <summary>The flat walkway over the tube's crown — the deck ground
    /// forms fight on and turrets stand on. Its own mesh and collider; the
    /// glowing edge lamps live in <see cref="BuildStationLamps"/>.</summary>
    void BuildRingDeck()
    {
        const int segments = 72;
        var vertices = new Vector3[(segments + 1) * 2];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];

        for (int s = 0; s <= segments; s++)
        {
            float a = s * (Mathf.PI * 2f / segments);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[s * 2] = dir * RingDeckInner;
            vertices[s * 2 + 1] = dir * RingDeckOuter;
            normals[s * 2] = normals[s * 2 + 1] = Vector3.up;
            uvs[s * 2] = new Vector2((float)s / segments, 0f);
            uvs[s * 2 + 1] = new Vector2((float)s / segments, 1f);
        }
        int i = 0;
        for (int s = 0; s < segments; s++)
        {
            int v = s * 2;
            triangles[i++] = v; triangles[i++] = v + 2; triangles[i++] = v + 1;
            triangles[i++] = v + 1; triangles[i++] = v + 2; triangles[i++] = v + 3;
        }

        var mesh = new Mesh { name = "StationDeck" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        var go = new GameObject("RingDeck");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, RingDeckY, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("dogfight-station-deck", new Color(0.17f, 0.2f, 0.28f), 0.35f);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Portholes: hard-edged glow rectangles around the tube's outer
    /// AND inner equators (the hole should glow when flown through), warm
    /// white with the odd cyan control room and the odd dark cabin. One mesh.</summary>
    void BuildStationWindows()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        Color warm = new Color(1f, 0.93f, 0.75f) * 1.5f;
        Color cool = GridGlow * 1.6f;

        var random = new System.Random(31);
        const int count = 72;
        foreach (float facing in new[] { 1f, -1f })
            foreach (float dy in new[] { -2.5f, 2.5f })
                for (int s = 0; s < count; s++)
                {
                    if (random.NextDouble() < 0.22)
                        continue;     // a dark cabin
                    float a = (s + (dy > 0f ? 0.5f : 0f)) * (Mathf.PI * 2f / count);
                    Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float reach = TorusMajor
                        + facing * (Mathf.Sqrt(TorusTube * TorusTube - dy * dy) + 0.15f);
                    Vector3 center = outward * reach + Vector3.up * (TorusY + dy);
                    Vector3 tangent = new Vector3(-outward.z, 0f, outward.x);
                    AddOrientedQuad(vertices, uvs, colors, triangles, center,
                        tangent * 0.9f, Vector3.up * 0.55f,
                        random.NextDouble() < 0.14 ? cool : warm);
                }

        FlatQuadMesh("StationWindows", null, vertices, uvs, colors, triangles);
    }

    /// <summary>The deck's own lamps: cyan and warm markers alternating along
    /// both walkway edges, and a hazard ring around the hub deck's rim.</summary>
    void BuildStationLamps()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        Color edgeA = GridGlow * 1.5f;
        Color edgeB = new Color(1f, 0.7f, 0.3f) * 1.4f;

        for (int s = 0; s < 36; s++)
        {
            float a = s * (Mathf.PI * 2f / 36f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Color c = s % 2 == 0 ? edgeA : edgeB;
            foreach (float r in new[] { RingDeckInner + 0.8f, RingDeckOuter - 0.8f })
                AddOrientedQuad(vertices, uvs, colors, triangles,
                    dir * r + Vector3.up * (RingDeckY + 0.15f),
                    Vector3.right * 0.65f, Vector3.forward * 0.65f, c);
            // The hub deck's rim, denser and all-cyan: the launch apron.
            AddOrientedQuad(vertices, uvs, colors, triangles,
                dir * (HubRadius - 1.2f) + Vector3.up * (HubTopY + 0.15f),
                Vector3.right * 0.55f, Vector3.forward * 0.55f, edgeA);
        }

        FlatQuadMesh("StationLamps", VfxUtil.GlowTexture, vertices, uvs, colors, triangles);
    }

    // ------------------------------------------------------------------ shared

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
    ///
    /// The airfield keeps its stars above the horizon (there is ground down
    /// there); the space maps wrap them the whole way round, because below
    /// the furniture there is only more sky.
    /// </summary>
    void BuildStars(bool fullDome)
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
        float floorDot = fullDome ? -1f : -0.05f;

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
                float y = Mathf.Lerp(floorDot, 1f, (float)random.NextDouble());
                float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
                float flat = Mathf.Sqrt(1f - y * y);
                dir = new Vector3(Mathf.Cos(azimuth) * flat, y, Mathf.Sin(azimuth) * flat);
            }
            if (dir.y < floorDot)
                dir.y = floorDot + ((float)random.NextDouble() * 0.3f);
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
        // The moon takes its year at a walking pace.
        if (_moonPivot != null)
            _moonPivot.Rotate(0f, 1.1f * Time.deltaTime, 0f);

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

    // ----------------------------------------------------------------- the deck

    /// <summary>
    /// What is underfoot at this spot — the height a landing, a walk, a drive
    /// or a wreck crash settles onto. The airfield is flat; the planet's deck
    /// is its curved surface (clamped at the rim so the flank behaves like a
    /// very steep hill rather than an abyss); the station is decks over open
    /// space, so the answer JUMPS at every edge — which is why grounded
    /// steps go through <see cref="HoldOnDeck"/> and landings refuse a deck
    /// that is far above them (see JetPawn.TouchDownCheck). Over a space
    /// map's void the number returned is bookkeeping, not ground — nothing
    /// is allowed to land on it (<see cref="InOpenSpace"/>).
    /// </summary>
    public static float GroundHeight(Vector3 at)
    {
        switch (Map)
        {
            case DogfightMapKind.TinyPlanet:
            {
                float d = Mathf.Min(new Vector2(at.x, at.z).magnitude, PlanetRim);
                return PlanetCenter.y
                       + Mathf.Sqrt(PlanetRadius * PlanetRadius - d * d);
            }
            case DogfightMapKind.DonutStation:
            {
                float d = new Vector2(at.x, at.z).magnitude;
                if (d < HubRadius - 1f)
                    return HubTopY;
                if (d >= RingDeckInner && d <= RingDeckOuter)
                    return RingDeckY;
                return VoidY;
            }
            default:
                return 0f;
        }
    }

    /// <summary>The lowest deck the queries may report: the airfield's
    /// tarmac, the planet's flank line, the station's void depth. On the
    /// space maps this is a backstop for the arithmetic, not a landing —
    /// falls out there drift instead of ending (<see cref="InOpenSpace"/>).</summary>
    public static float BottomHeight
    {
        get
        {
            switch (Map)
            {
                case DogfightMapKind.TinyPlanet:
                    return PlanetCenter.y + Mathf.Sqrt(
                        PlanetRadius * PlanetRadius - PlanetRim * PlanetRim);
                case DogfightMapKind.DonutStation:
                    return VoidY;
                default:
                    return 0f;
            }
        }
    }

    /// <summary>
    /// The deck this mover is actually OVER: <see cref="GroundHeight"/> when
    /// the mover is above it, the map's <see cref="BottomHeight"/> once it has
    /// fallen past. This is the query every ground CONTACT uses — a jet
    /// threading under the station's walkway is over the net, not the
    /// walkway, and must never be snapped up through the furniture.
    /// </summary>
    public static float DeckUnder(Vector3 at)
    {
        float deck = GroundHeight(at);
        return at.y >= deck - 3f ? deck : BottomHeight;
    }

    /// <summary>True where the deck under this spot is not real furniture —
    /// the station's empty space, the planet's flank clamp. Wreck craters
    /// skip their fire here, AI pilots refuse to fold into ground forms over
    /// it, and on the space maps it is where gravity ends.</summary>
    public static bool OverVoid(Vector3 at)
    {
        switch (Map)
        {
            case DogfightMapKind.TinyPlanet:
                return new Vector2(at.x, at.z).magnitude > PlanetRim - 2f;
            case DogfightMapKind.DonutStation:
                return GroundHeight(at) == VoidY;
            default:
                return false;
        }
    }

    /// <summary>
    /// True when this spot hangs in OPEN SPACE: a space map, over its void.
    /// Out here there is no ground and no gravity — fall physics trades its
    /// downward pull for a drift, landings are refused, and a wreck ends as
    /// a blast adrift instead of a crash site. Over the furniture (the
    /// planet's dome, the station's decks) local gravity still pulls, which
    /// is exactly what lets a tank fold out of the sky and LAND there.
    /// </summary>
    public static bool InOpenSpace(Vector3 at) => FreeOrientation && OverVoid(at);

    /// <summary>
    /// The grounded step test: a walking robot or driving tank may follow the
    /// deck up and down honest slopes, but a step whose deck leaps — a
    /// walkway edge, the planet's flank going vertical — is refused, keeping
    /// the mover on its ledge instead of teleporting down a cliff or up onto
    /// one. Slope-based so the planet's dome walks naturally while its flank
    /// (and every station edge) reads as a wall.
    /// </summary>
    public static Vector3 HoldOnDeck(Vector3 from, Vector3 to)
    {
        float step = new Vector2(to.x - from.x, to.z - from.z).magnitude;
        float delta = Mathf.Abs(DeckUnder(to) - DeckUnder(from));
        if (delta <= Mathf.Max(0.6f, step * 1.2f))
            return to;
        return new Vector3(from.x, to.y, from.z);
    }

    /// <summary>The local soft floor the assist lifts away from: flat on the
    /// airfield, the SURFACE plus a margin on the planet (so the fight bends
    /// around the dome), and low on the station map so the hole and the
    /// under-ring pass stay flyable.</summary>
    static float LocalFloor(Vector3 position)
    {
        switch (Map)
        {
            case DogfightMapKind.TinyPlanet:
                return GroundHeight(position) + PlanetFloorLift;
            case DogfightMapKind.DonutStation:
                return DonutFloorY;
            default:
                return FloorY;
        }
    }

    // --------------------------------------------------------------- the volume

    /// <summary>
    /// True where the map is a body hanging in space rather than ground under a
    /// sky. The planet is a real sphere and the station a real torus, so "below"
    /// them is not out of bounds — it is the other side. On those maps there is
    /// no floor, no ceiling and no level: the volume is a shell, and a jet may
    /// fly any attitude through it.
    ///
    /// The airfield stays as it was. It has a runway, a takeoff and a landing,
    /// and a horizon to level against — a fixed up is the right model there.
    /// </summary>
    public static bool FreeOrientation => Map != DogfightMapKind.Airfield;

    /// <summary>Centre of the body the shell is drawn around.</summary>
    public static Vector3 SpaceCenter =>
        Map == DogfightMapKind.TinyPlanet ? PlanetCenter : Vector3.zero;

    /// <summary>Closest a jet may come to the body's centre — the surface plus
    /// room to pull out of a dive at boost.</summary>
    public static float ShellInner =>
        Map == DogfightMapKind.TinyPlanet ? PlanetRadius + PlanetFloorLift : RingDeckOuter * 0.55f;

    /// <summary>The far edge of the fight, measured from the same centre. Sized
    /// so the planet map keeps roughly the roaming room the cylinder gave it,
    /// now available all the way around instead of over the top only.</summary>
    public static float ShellOuter =>
        Map == DogfightMapKind.TinyPlanet ? PlanetRadius + 150f : Radius;

    /// <summary>
    /// Shell assist: the space maps' answer to the fence, floor and ceiling in
    /// one. Distance from the body's centre is the only thing that matters, so
    /// the same rule holds whichever way up a jet is flying.
    ///
    /// Both nudges are applied as PITCH, in the jet's own frame — pull toward
    /// open space, push away from the body. Yaw would be meaningless here: with
    /// free orientation there is no world-horizontal plane for it to turn in.
    /// </summary>
    static Vector2 ShellAssist(Vector3 position, Vector3 forward, Vector3 up, Vector2 steer)
    {
        Vector3 out0 = position - SpaceCenter;
        float distance = out0.magnitude;
        if (distance < 1e-3f)
            return steer;
        Vector3 outward = out0 / distance;

        // Which way the nose would have to swing, expressed as a pitch sign in
        // the jet's own frame: +1 pulls toward the jet's back, -1 toward its
        // belly. Dotting the wanted direction against the jet's up gives that
        // directly, and stays correct upside down.
        float band = AssistBand * 0.65f;

        float below = (ShellInner + band) - distance;      // too close to the body
        if (below > 0f)
        {
            float grip = Mathf.Clamp01(below / band);
            if (Vector3.Dot(forward, outward) < 0.85f)
                steer.y = Mathf.Lerp(steer.y, Mathf.Sign(Vector3.Dot(up, outward)), grip * grip);
        }

        float beyond = distance - (ShellOuter - AssistBand);   // drifting out of the fight
        if (beyond > 0f)
        {
            float grip = Mathf.Clamp01(beyond / AssistBand);
            if (Vector3.Dot(forward, -outward) < 0.85f)
                steer.y = Mathf.Lerp(steer.y, Mathf.Sign(Vector3.Dot(up, -outward)), grip * grip);
        }

        return steer;
    }

    /// <summary>
    /// The sky's hand on the stick: blends the driver's steer toward "back
    /// inside" as a jet nears the edge, the floor or the ceiling, reaching full
    /// authority at the boundary itself. Centripetal rather than a hard flip —
    /// the yaw component pushes toward the centre's side, so an edge approach
    /// becomes a wide banked turn back into the fight.
    ///
    /// Space maps answer through <see cref="ShellAssist"/> instead: there is no
    /// fence, floor or ceiling to lean away from, only a distance from the body.
    /// </summary>
    public static Vector2 SteerAssist(Vector3 position, Vector3 forward, Vector3 up, Vector2 steer)
    {
        if (FreeOrientation)
            return ShellAssist(position, forward, up, steer);
        return SteerAssist(position, forward, steer);
    }

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
        // shallower than the fence's: altitude mistakes happen faster. The
        // floor is the MAP's — flat over the airfield, bent around the
        // planet's dome, dropped under the station's ring.
        float floor = LocalFloor(position);
        float lift = Mathf.Clamp01((floor + 22f - position.y) / 22f);
        if (lift > 0f)
            steer.y = Mathf.Lerp(steer.y, 1f, lift * lift);
        float duck = Mathf.Clamp01((position.y - (CeilingY - 22f)) / 22f);
        if (duck > 0f)
            steer.y = Mathf.Lerp(steer.y, -1f, duck * duck);

        return steer;
    }

    /// <summary>
    /// Keep movers out of the furniture. A positional push, TankPawn's
    /// AvoidScenery reasoning in the air: everything here moves by writing its
    /// own transform, so Unity's collision response never runs, and cover you
    /// can be shot around but flown through is a rule nobody can learn.
    ///
    /// Grounded movers skip the big-body pushes — their y belongs to
    /// <see cref="GroundHeight"/> and their edges to <see cref="HoldOnDeck"/>;
    /// a radial shove fighting the deck clamp is a jitter machine. They keep
    /// the spire-style column pushes, which are purely horizontal.
    /// </summary>
    public static Vector3 KeepOffProps(Vector3 position, float clearance,
        bool grounded = false)
    {
        if (!grounded)
        {
            switch (Map)
            {
                case DogfightMapKind.TinyPlanet:
                {
                    // The planet is one big prop: a radial push off the sphere,
                    // which is what lets low passes skim the flank as happily
                    // as the pole.
                    Vector3 gap = position - PlanetCenter;
                    float want = PlanetRadius + clearance;
                    float distance = gap.magnitude;
                    if (distance < want && distance > 1e-3f)
                        position = PlanetCenter + gap / distance * want;
                    break;
                }
                case DogfightMapKind.DonutStation:
                    position = PushOffStation(position, clearance);
                    break;
            }
        }

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

    /// <summary>The station's solids, in flight terms: the tube (nearest
    /// point on the ring's spine, pushed out of the cross-section), the hub
    /// drum (pushed out its nearest face), the four spokes (capsule tests)
    /// and the mast under the hub. All constant geometry — a mid-Play
    /// recompile cannot forget it.</summary>
    static Vector3 PushOffStation(Vector3 position, float clearance)
    {
        // The tube.
        Vector2 flat = new Vector2(position.x, position.z);
        float d = flat.magnitude;
        Vector3 spine = d > 1e-3f
            ? new Vector3(position.x / d * TorusMajor, TorusY, position.z / d * TorusMajor)
            : new Vector3(TorusMajor, TorusY, 0f);
        Vector3 gap = position - spine;
        float want = TorusTube + clearance;
        float distance = gap.magnitude;
        if (distance < want)
            position = spine + (distance > 1e-3f ? gap / distance : Vector3.up) * want;

        // The hub drum: push out whichever face is nearest. "Above the top"
        // is judged with a capped margin so a jet may skim the deck as low as
        // it skims everything else.
        d = new Vector2(position.x, position.z).magnitude;
        float topMargin = Mathf.Min(clearance, 1.0f);
        if (d < HubRadius + clearance
            && position.y > HubBottomY - clearance && position.y < HubTopY + topMargin)
        {
            float side = HubRadius + clearance - d;
            float up = HubTopY + topMargin - position.y;
            float down = position.y - (HubBottomY - clearance);
            if (side <= up && side <= down && d > 1e-3f)
                position = new Vector3(position.x / d * (HubRadius + clearance),
                    position.y, position.z / d * (HubRadius + clearance));
            else if (up <= down)
                position = new Vector3(position.x, HubTopY + topMargin, position.z);
            else
                position = new Vector3(position.x, HubBottomY - clearance, position.z);
        }

        // The spokes: horizontal capsules from hub to ring.
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = i == 0 ? Vector3.forward
                : i == 1 ? Vector3.right
                : i == 2 ? Vector3.back
                : Vector3.left;
            float along = Mathf.Clamp(Vector3.Dot(position, dir), 32f, 60f);
            Vector3 axis = dir * along + Vector3.up * TorusY;
            Vector3 offset = position - axis;
            float reach = SpokeGirth + clearance;
            float off = offset.magnitude;
            if (off < reach && off > 1e-3f)
                position = axis + offset / off * reach;
        }

        // The mast hanging under the hub.
        d = new Vector2(position.x, position.z).magnitude;
        if (position.y < HubBottomY && position.y > HubBottomY - 14f
            && d < 2.5f + clearance && d > 1e-3f)
            position = new Vector3(position.x / d * (2.5f + clearance),
                position.y, position.z / d * (2.5f + clearance));

        return position;
    }
}
