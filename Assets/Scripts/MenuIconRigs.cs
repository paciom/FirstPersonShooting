using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>The main menu's app icons, in grid order. Indexes MenuIconSet.textures.</summary>
public enum MenuIcon
{
    AIvAI = 0, PlayerVsAI, Brawl, BrawlWar, BrawlShow,
    OnlinePvP, Commander, CommanderWar, TowerDefense, ChineseQuest, ArenaBuilder,
}

/// <summary>What MenuIconRigs.Build hands back: the rig root plus everything
/// that must be released when the menu dies.</summary>
public class MenuIconSet
{
    public GameObject root;
    public RenderTexture[] textures;
    public Camera[] cameras;
}

/// <summary>
/// Builds the main menu's "app icon" previews: one tiny FROZEN 3D diorama per
/// game mode — two robots trading fire for AI v AI, a mid-punch/mid-kick pair
/// for Brawl, a little base for Commander, a canyon with marching raiders for
/// Tower Defense — each rendered by its own camera into a RenderTexture that
/// the menu shows inside a rounded icon tile.
///
/// Frozen on purpose: every robot is posed once through its Brawl animator
/// (Play + Update(0) + disable), so the icons are statues, not ten looping
/// animations. The cameras DO render continuously while the menu is up — that
/// is the same budget the robot-select screen spends on 18 live cards — and
/// stop the moment the menu canvas deactivates (see MenuIconRigSync).
///
/// The rigs live far below every world this game builds: arena/Commander/TD
/// ground is y=0, the select screen's rigs hang at -150..-210, these at -300.
/// </summary>
public static class MenuIconRigs
{
    public const int IconCount = 11;

    // Far under everything, spaced so no rig's lights (max range ~13) or
    // camera far plane (max 15) can reach a neighbour 40 units away.
    const float RigDepth = -300f;
    const float RigSpacing = 40f;
    const int TextureSize = 320;

    // Fight-scale dioramas reuse the select screen's exact studio (reach 1);
    // the strategy tables are ~2x wider and get the same rig scaled out.
    const float FightReach = 1f;
    const float TableReach = 2.2f;

    static readonly Color Cyan = MatchAnnouncer.TeamColor(0);
    static readonly Color Magenta = MatchAnnouncer.TeamColor(1);

    public static MenuIconSet Build(Transform parent, RobotRoster roster)
    {
        var root = new GameObject("MenuIconRigs");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(0f, RigDepth, 0f);

        var set = new MenuIconSet
        {
            root = root,
            textures = new RenderTexture[IconCount],
            cameras = new Camera[IconCount],
        };

        for (int i = 0; i < IconCount; i++)
        {
            var rig = new GameObject($"IconRig_{(MenuIcon)i}").transform;
            rig.SetParent(root.transform, false);
            rig.localPosition = new Vector3(i * RigSpacing, 0f, 0f);
            set.textures[i] = new RenderTexture(TextureSize, TextureSize, 16)
            {
                name = $"MenuIcon_{(MenuIcon)i}",
                antiAliasing = 2,
            };
            set.cameras[i] = BuildDiorama((MenuIcon)i, rig, roster, set.textures[i]);
        }
        return set;
    }

    static Camera BuildDiorama(MenuIcon icon, Transform rig, RobotRoster roster, RenderTexture rt)
    {
        switch (icon)
        {
            case MenuIcon.AIvAI: return BuildAIvAI(rig, roster, rt);
            case MenuIcon.PlayerVsAI: return BuildPlayerVsAI(rig, roster, rt);
            case MenuIcon.Brawl: return BuildBrawl(rig, roster, rt);
            case MenuIcon.BrawlWar: return BuildBrawlWar(rig, roster, rt);
            case MenuIcon.BrawlShow: return BuildBrawlShow(rig, roster, rt);
            case MenuIcon.OnlinePvP: return BuildOnline(rig, roster, rt);
            case MenuIcon.Commander: return BuildCommander(rig, roster, rt);
            case MenuIcon.CommanderWar: return BuildCommanderWar(rig, roster, rt);
            case MenuIcon.TowerDefense: return BuildTowerDefense(rig, roster, rt);
            case MenuIcon.ChineseQuest: return BuildChineseQuest(rig, roster, rt);
            default: return BuildArenaBuilder(rig, rt);
        }
    }

    // ------------------------------------------------------------ dioramas

    /// <summary>Two robots mid-laser-duel, side on: the classic arena match.</summary>
    static Camera BuildAIvAI(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);
        FrozenRobot(rig, Cast(roster, "ranger", 0), 0, BrawlAnim.Blast, 0.45f,
            new Vector3(-0.68f, 0f, 0f), 90f);
        FrozenRobot(rig, Cast(roster, "titan", 1), 1, BrawlAnim.Blast, 0.45f,
            new Vector3(0.68f, 0f, 0f), -90f);
        Bolt(rig, new Vector3(-0.28f, 0.92f, 0.04f), new Vector3(0.46f, 0.9f, 0.04f), Cyan);
        Bolt(rig, new Vector3(0.28f, 0.7f, -0.06f), new Vector3(-0.46f, 0.72f, -0.06f), Magenta);
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.05f, 2.9f), 8f, 40f, 12f);
    }

    /// <summary>Over the cyan robot's shoulder, blasting a recoiling enemy: you, in the fight.</summary>
    static Camera BuildPlayerVsAI(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);
        FrozenRobot(rig, Cast(roster, "scout", 2), 0, BrawlAnim.Blast, 0.5f,
            new Vector3(-0.42f, 0f, 1.05f), 168f);
        FrozenRobot(rig, Cast(roster, "knight", 3), 1, BrawlAnim.Hit, 0.35f,
            new Vector3(0.38f, 0f, -0.75f), 12f);
        Bolt(rig, new Vector3(-0.18f, 0.95f, 0.6f), new Vector3(0.3f, 0.85f, -0.5f), Cyan);
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.15f, 3.05f), 10f, 42f, 12f);
    }

    /// <summary>The user's brief, verbatim: two robots frozen in fighting poses.</summary>
    static Camera BuildBrawl(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);
        FrozenRobot(rig, Cast(roster, "samurai", 4), 0, "Punch", 0.5f,
            new Vector3(-0.6f, 0f, 0f), 90f);
        FrozenRobot(rig, Cast(roster, "panther", 5), 1, "KickHigh", 0.5f,
            new Vector3(0.6f, 0f, 0f), -90f);
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.05f, 2.9f), 8f, 40f, 12f);
    }

    /// <summary>Two flying kicks crossing mid-air — the exhibition bout, all spectacle.</summary>
    static Camera BuildBrawlWar(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);
        FrozenRobot(rig, Cast(roster, "hawk", 6), 0, BrawlAnim.FlyKick, 0.5f,
            new Vector3(-0.72f, 0f, 0f), 90f);
        FrozenRobot(rig, Cast(roster, "racer", 7), 1, BrawlAnim.FlyKick, 0.55f,
            new Vector3(0.72f, 0f, 0f), -90f);
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.05f, 2.9f), 8f, 40f, 12f);
    }

    /// <summary>One performer alone on a glowing stage, frozen mid spin kick.</summary>
    static Camera BuildBrawlShow(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        var floor = ArenaMaterials.Lit("MenuIcon_Floor", new Color(0.16f, 0.2f, 0.25f), 0.35f);
        Prop(rig, PrimitiveType.Cylinder, new Vector3(0f, -0.02f, 0f),
            new Vector3(2f, 0.02f, 2f), floor);
        // The rim: a slightly wider emissive disc just below the stage top, so
        // its edge glows around the platform from the camera's low angle.
        Prop(rig, PrimitiveType.Cylinder, new Vector3(0f, -0.032f, 0f),
            new Vector3(2.16f, 0.012f, 2.16f),
            ArenaMaterials.Emissive("MenuIcon_StageRim", Cyan, 1.5f));
        FrozenRobot(rig, Cast(roster, "bolt", 8), 0, "KickSpin", 0.5f,
            new Vector3(0f, 0f, 0.1f), -30f, 1.65f);
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 0.95f, 2.7f), 8f, 38f, 12f);
    }

    /// <summary>Two robots squared up under a little ringed planet: a match across the world.</summary>
    static Camera BuildOnline(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);
        FrozenRobot(rig, Cast(roster, "ranger", 0), 0, null, 0f,
            new Vector3(-0.82f, 0f, 0.1f), 90f);
        FrozenRobot(rig, Cast(roster, "knight", 3), 1, null, 0f,
            new Vector3(0.82f, 0f, 0.1f), -90f);

        var planetPos = new Vector3(0f, 1.62f, -0.25f);
        Prop(rig, PrimitiveType.Sphere, planetPos, Vector3.one * 0.56f,
            ArenaMaterials.Emissive("MenuIcon_Planet", new Color(0.3f, 0.78f, 1f), 1.4f));
        Prop(rig, PrimitiveType.Cylinder, planetPos, new Vector3(1.24f, 0.012f, 1.24f),
            ArenaMaterials.Emissive("MenuIcon_PlanetRing", Cyan, 1.5f),
            new Vector3(14f, 0f, 18f));
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.05f, 3f), 4f, 44f, 12f);
    }

    /// <summary>A working base from the RTS camera: buildings up, a patrol out front.</summary>
    static Camera BuildCommander(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        TablePlate(rig, 3.4f);
        Building(rig, "command", 1.05f, new Vector3(-0.55f, 0f, -0.45f), 12f);
        Building(rig, "factory", 0.78f, new Vector3(0.72f, 0f, -0.5f), -8f);
        Building(rig, "power", 0.5f, new Vector3(-1.05f, 0f, 0.42f), 0f);
        Building(rig, "turret", 0.44f, new Vector3(0.28f, 0f, 0.5f), 0f);
        FrozenRobot(rig, Cast(roster, "ranger", 0), 0, null, 0f,
            new Vector3(0.62f, 0f, 1.05f), 135f, 0.42f);
        FrozenRobot(rig, Cast(roster, "scout", 2), 0, null, 0f,
            new Vector3(0.9f, 0f, 0.85f), 145f, 0.42f);
        FrozenRobot(rig, Cast(roster, "titan", 1), 0, null, 0f,
            new Vector3(1.12f, 0f, 1.12f), 150f, 0.42f);
        AddLights(rig, TableReach);
        return AddCamera(rig, rt, new Vector3(0f, 3.2f, 3.5f), 43f, 34f, 15f);
    }

    /// <summary>Two bases, two armies, bolts crossing the midfield: the machine war.</summary>
    static Camera BuildCommanderWar(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        TablePlate(rig, 3.6f);
        Building(rig, "command", 0.8f, new Vector3(-1.15f, 0f, -0.35f), 20f);
        Building(rig, "command", 0.8f, new Vector3(1.15f, 0f, -0.35f), -20f);
        TeamRing(rig, new Vector3(-1.15f, 0f, -0.35f), 1.15f, Cyan);
        TeamRing(rig, new Vector3(1.15f, 0f, -0.35f), 1.15f, Magenta);

        FrozenRobot(rig, Cast(roster, "ranger", 0), 0, null, 0f, new Vector3(-0.52f, 0f, 0.5f), 90f, 0.4f);
        FrozenRobot(rig, Cast(roster, "scout", 2), 0, null, 0f, new Vector3(-0.74f, 0f, 0.76f), 90f, 0.4f);
        FrozenRobot(rig, Cast(roster, "titan", 1), 0, null, 0f, new Vector3(-0.62f, 0f, 0.24f), 90f, 0.4f);
        FrozenRobot(rig, Cast(roster, "knight", 3), 1, null, 0f, new Vector3(0.52f, 0f, 0.55f), -90f, 0.4f);
        FrozenRobot(rig, Cast(roster, "panther", 5), 1, null, 0f, new Vector3(0.74f, 0f, 0.3f), -90f, 0.4f);
        FrozenRobot(rig, Cast(roster, "hawk", 6), 1, null, 0f, new Vector3(0.64f, 0f, 0.8f), -90f, 0.4f);

        Bolt(rig, new Vector3(-0.3f, 0.24f, 0.5f), new Vector3(0.32f, 0.22f, 0.56f), Cyan, 0.035f);
        Bolt(rig, new Vector3(0.3f, 0.2f, 0.3f), new Vector3(-0.32f, 0.23f, 0.34f), Magenta, 0.035f);
        AddLights(rig, TableReach);
        return AddCamera(rig, rt, new Vector3(0f, 3.2f, 3.5f), 43f, 34f, 15f);
    }

    /// <summary>Raiders marching a canyon toward the Core, towers waiting on the ledges.</summary>
    static Camera BuildTowerDefense(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        var sand = ArenaMaterials.Lit("MenuIcon_Sand", new Color(0.4f, 0.35f, 0.29f), 0.1f);
        var rock = ArenaMaterials.Lit("MenuIcon_Rock", new Color(0.34f, 0.28f, 0.23f), 0.12f);
        Prop(rig, PrimitiveType.Cube, new Vector3(0f, -0.04f, 0f), new Vector3(3.4f, 0.08f, 3.8f), sand);
        Prop(rig, PrimitiveType.Cube, new Vector3(-1.15f, 0.3f, 0f), new Vector3(0.85f, 0.6f, 3.6f), rock);
        Prop(rig, PrimitiveType.Cube, new Vector3(1.15f, 0.3f, 0f), new Vector3(0.85f, 0.6f, 3.6f), rock);

        // Towers hold the high ground; the Core glows at the canyon's end.
        Building(rig, "turret", 0.45f, new Vector3(-1.1f, 0.6f, 0.45f), 10f);
        Building(rig, "tech", 0.55f, new Vector3(1.1f, 0.6f, -0.5f), -14f);
        Building(rig, "command", 0.7f, new Vector3(0f, 0f, -1.4f), 0f);
        TeamRing(rig, new Vector3(0f, 0f, -1.4f), 1f, Cyan);

        FrozenRobot(rig, Cast(roster, "racer", 7), 1, null, 0f, new Vector3(0.06f, 0f, 0.95f), 180f, 0.4f);
        FrozenRobot(rig, Cast(roster, "bolt", 8), 1, null, 0f, new Vector3(-0.1f, 0f, 0.35f), 180f, 0.4f);
        FrozenRobot(rig, Cast(roster, "scout", 2), 1, null, 0f, new Vector3(0.12f, 0f, -0.25f), 180f, 0.4f);
        AddLights(rig, TableReach);
        return AddCamera(rig, rt, new Vector3(0f, 3f, 3.7f), 40f, 35f, 15f);
    }

    /// <summary>
    /// A character hanging in the air over a hero robot blasting one of the
    /// two answers flanking it — the quiz, in one frame. The glyph is the
    /// icon: no arrangement of robots says "Chinese" and a single 汉 says
    /// nothing else.
    /// </summary>
    static Camera BuildChineseQuest(Transform rig, RobotRoster roster, RenderTexture rt)
    {
        FightFloor(rig);

        // The two answers, small and set back so the hero reads as the one
        // the player is.
        FrozenRobot(rig, Cast(roster, "knight", 3), 1, BrawlAnim.Hit, 0.35f,
            new Vector3(-1.02f, 0f, -0.2f), 130f, 1.1f);
        FrozenRobot(rig, Cast(roster, "panther", 5), 1, null, 0f,
            new Vector3(1.02f, 0f, -0.2f), -130f, 1.1f);
        FrozenRobot(rig, Cast(roster, "ranger", 0), 0, BrawlAnim.Blast, 0.5f,
            new Vector3(0.05f, 0f, 0.72f), 200f, 1.35f);

        Bolt(rig, new Vector3(-0.2f, 0.78f, 0.5f), new Vector3(-0.88f, 0.62f, -0.1f), Cyan);
        HoloGlyph(rig, "汉", new Vector3(0f, 1.42f, -0.5f), 0.8f);

        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.2f, 3.0f), 10f, 40f, 12f);
    }

    /// <summary>
    /// A Chinese character hanging in the diorama as a hologram.
    ///
    /// A world-space Canvas rather than a TextMesh: these glyphs come from a
    /// DYNAMIC font (Noto Sans SC, rasterized on demand), and the UI text
    /// path is the one that rebuilds itself correctly when that atlas grows.
    /// The rig sits 300 units under the world with a 12-unit camera far
    /// plane, so only its own icon camera can see it.
    /// </summary>
    static void HoloGlyph(Transform rig, string glyph, Vector3 localPosition, float height)
    {
        var go = new GameObject("HoloGlyph");
        go.transform.SetParent(rig, false);

        // Adding a Canvas turns this object's Transform INTO the RectTransform,
        // so the scale below is the object's own — which is why the glow plate
        // hangs off the rig instead of off this, where it would be scaled by
        // the canvas-units-to-metres factor as well as by its own size.
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.localPosition = localPosition;
        rect.sizeDelta = new Vector2(200f, 200f);
        // Canvas units to diorama metres. The camera looks back down -Z, which
        // is the face a canvas presents by default, so no billboarding.
        rect.localScale = Vector3.one * (height / 200f);

        var label = ChineseFont.MakeText(go.transform, "Glyph", glyph, 170,
            new Color(0.75f, 0.97f, 1f), FontStyle.Bold);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        // The glow BEHIND it — further from the camera, which sits at +Z — so
        // the character reads as projected light rather than as a sticker.
        // 1.1 emission stays under the bloom whiteout line.
        Prop(rig, PrimitiveType.Quad, localPosition + new Vector3(0f, 0f, -0.06f),
            Vector3.one * (height * 1.25f),
            ArenaMaterials.Emissive("MenuIcon_GlyphGlow", new Color(0.12f, 0.55f, 0.8f), 1.1f));
    }

    /// <summary>A toybox of arena blocks with one ghost-block floating mid-placement.</summary>
    static Camera BuildArenaBuilder(Transform rig, RenderTexture rt)
    {
        TablePlate(rig, 2.8f);
        var hull = ArenaMaterials.Style("MenuIcon_Block", ArenaMaterials.SurfaceStyle.Hull,
            new Color(0.42f, 0.5f, 0.6f), new Color(0.28f, 0.34f, 0.43f), 0.8f, 0.55f);
        Prop(rig, PrimitiveType.Cube, new Vector3(-0.45f, 0.31f, -0.1f), Vector3.one * 0.62f, hull);
        Prop(rig, PrimitiveType.Cube, new Vector3(0.42f, 0.25f, -0.45f), Vector3.one * 0.5f, hull,
            new Vector3(0f, 18f, 0f));
        Prop(rig, PrimitiveType.Cylinder, new Vector3(0.62f, 0.42f, 0.35f),
            new Vector3(0.34f, 0.42f, 0.34f), hull);
        Prop(rig, PrimitiveType.Cube, new Vector3(-0.45f, 0.645f, 0.21f),
            new Vector3(0.64f, 0.05f, 0.05f),
            ArenaMaterials.Emissive("MenuIcon_TrimCyan", Cyan, 1.6f));
        Prop(rig, PrimitiveType.Cube, new Vector3(0.05f, 0.11f, 0.18f), Vector3.one * 0.22f,
            ArenaMaterials.Emissive("MenuIcon_TrimMagenta", Magenta, 1.4f));
        // The block being placed: hovering, tilted, holographic.
        Prop(rig, PrimitiveType.Cube, new Vector3(0f, 1.22f, -0.05f), Vector3.one * 0.4f,
            ArenaMaterials.Emissive("MenuIcon_Ghost", new Color(0.35f, 0.9f, 1f), 1.1f),
            new Vector3(12f, 25f, 8f));
        AddLights(rig, FightReach);
        return AddCamera(rig, rt, new Vector3(0f, 1.35f, 2.9f), 17f, 40f, 12f);
    }

    // ------------------------------------------------------------- helpers

    /// <summary>The shared round floor every fight-scale diorama stands on.</summary>
    static void FightFloor(Transform rig)
    {
        Prop(rig, PrimitiveType.Cylinder, new Vector3(0f, -0.02f, 0f), new Vector3(3f, 0.02f, 3f),
            ArenaMaterials.Lit("MenuIcon_Floor", new Color(0.16f, 0.2f, 0.25f), 0.35f));
    }

    /// <summary>The square ground plate the strategy dioramas sit on.</summary>
    static void TablePlate(Transform rig, float size)
    {
        Prop(rig, PrimitiveType.Cube, new Vector3(0f, -0.04f, 0f), new Vector3(size, 0.08f, size),
            ArenaMaterials.Lit("MenuIcon_Table", new Color(0.15f, 0.19f, 0.24f), 0.3f));
    }

    /// <summary>Flat emissive allegiance disc under a building, in lieu of a repaint.</summary>
    static void TeamRing(Transform rig, Vector3 groundPos, float diameter, Color color)
    {
        Prop(rig, PrimitiveType.Cylinder, groundPos + new Vector3(0f, 0.005f, 0f),
            new Vector3(diameter, 0.006f, diameter),
            ArenaMaterials.Emissive($"MenuIcon_Ring_{ColorUtility.ToHtmlStringRGB(color)}", color, 1.2f));
    }

    /// <summary>
    /// A roster robot frozen in a named Brawl pose. The fighter prefab (re-rigged
    /// Meshy skeleton, Brawl controller baked in) is preferred; the FPS walker is
    /// the fallback. Null <paramref name="state"/> — or a state this robot's
    /// controller lacks — freezes the default stance instead.
    ///
    /// Painted at TeamPaint.DefaultSize deliberately: those repaints are the ones
    /// the arena itself makes and keeps, so the icons share them for free — and
    /// they are immune to the select screen's Release(CardSize) on close, which
    /// would yank a card-sized texture out from under a live icon.
    /// </summary>
    static GameObject FrozenRobot(Transform parent, RobotRoster.Entry entry, int teamId,
        string state, float normalizedTime, Vector3 pos, float yaw, float height = 1.6f)
    {
        if (entry.modelPrefab == null)
            return null;

        var holder = new GameObject($"Robot_{entry.displayName}").transform;
        holder.SetParent(parent, false);
        holder.localPosition = pos;
        holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
        // InstantiateNormalized fits every robot to 1.6 units; the holder's own
        // scale takes it from there to this diorama's size.
        holder.localScale = Vector3.one * (height / 1.6f);

        string robot = RobotName(entry);
        var fighterPrefab = Resources.Load<GameObject>($"Brawl/{robot}-fighter");
        var model = RobotFactory.InstantiateNormalized(
            fighterPrefab != null ? fighterPrefab : entry.modelPrefab, holder,
            MatchAnnouncer.TeamColor(teamId), entry.paintAnchorHue);

        // Feet on the floor whichever grounding branch InstantiateNormalized
        // took (it centres rigs without a RobotLocomotion on the pivot).
        // Measured through bind poses BEFORE posing — Renderer.bounds lies on
        // these rigs, and a kick pose would distort the fit anyway.
        var bounds = RobotFactory.MeasureWorldBounds(model.GetComponentsInChildren<Renderer>());
        if (bounds.size.sqrMagnitude > 1e-6f)
            model.transform.localPosition -= holder.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));

        var locomotion = model.GetComponentInChildren<RobotLocomotion>(true);
        if (locomotion != null)
            locomotion.enabled = false;

        var animator = model.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            // Explicit Update() evaluates regardless, but never leave the pose
            // hostage to culling heuristics on an off-screen rig.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!string.IsNullOrEmpty(state))
            {
                int hash = Animator.StringToHash(state);
                if (animator.HasState(0, hash))
                    animator.Play(hash, 0, normalizedTime);
            }
            animator.Update(0f);
            animator.enabled = false;   // the freeze
        }
        return model;
    }

    /// <summary>"ranger" from "ranger-robot" — the same convention BrawlFighter keys off.</summary>
    static string RobotName(RobotRoster.Entry entry)
    {
        string name = entry.modelPrefab.name;
        return name.EndsWith("-robot") ? name.Substring(0, name.Length - "-robot".Length) : name;
    }

    /// <summary>Roster entry by robot name, falling back to an index so the icons
    /// still cast SOMETHING from a roster that lacks the named robot.</summary>
    static RobotRoster.Entry Cast(RobotRoster roster, string robot, int fallback)
    {
        if (roster == null || !roster.HasRobots)
            return default;
        foreach (var entry in roster.robots)
            if (entry.modelPrefab != null && entry.modelPrefab.name.StartsWith(robot))
                return entry;
        return roster.Get(fallback);
    }

    /// <summary>
    /// A Commander/TD building GLB fitted so its widest ground dimension is
    /// <paramref name="size"/>, grounded at <paramref name="groundPos"/>. Falls
    /// back to a plain block so a missing model can never blank an icon.
    /// </summary>
    static void Building(Transform parent, string key, float size, Vector3 groundPos, float yaw)
    {
        var holder = new GameObject($"Building_{key}").transform;
        holder.SetParent(parent, false);
        holder.localPosition = groundPos;
        holder.localRotation = Quaternion.Euler(0f, yaw, 0f);

        var prefab = Resources.Load<GameObject>($"Buildings/{key}-building");
        if (prefab == null)
        {
            Prop(holder, PrimitiveType.Cube, new Vector3(0f, size * 0.45f, 0f),
                new Vector3(size * 0.8f, size * 0.9f, size * 0.8f),
                ArenaMaterials.Lit("MenuIcon_BuildingBlock", new Color(0.3f, 0.36f, 0.45f), 0.3f));
            return;
        }

        var instance = Object.Instantiate(prefab, holder);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;
        var bounds = RobotFactory.MeasureWorldBounds(renderers);
        float widest = Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
        // Multiply, never replace: glTF roots carry unit-conversion scale.
        instance.transform.localScale *= size / widest;

        bounds = RobotFactory.MeasureWorldBounds(instance.GetComponentsInChildren<Renderer>());
        instance.transform.localPosition -= holder.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
    }

    /// <summary>A frozen laser bolt: an emissive box stretched from A to B.</summary>
    static void Bolt(Transform parent, Vector3 from, Vector3 to, Color color, float thickness = 0.05f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Bolt";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (from + to) * 0.5f;
        go.transform.localRotation = Quaternion.LookRotation(to - from);
        go.transform.localScale = new Vector3(thickness, thickness, (to - from).magnitude);
        go.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Emissive(
            $"MenuIcon_Bolt_{ColorUtility.ToHtmlStringRGB(color)}", color, 1.8f);
    }

    static GameObject Prop(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale,
        Material material, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = $"Prop_{type}";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (euler.HasValue)
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>
    /// The select screen's three-point studio, scaled out by <paramref name="reach"/>
    /// for the wider strategy tables: positions and range scale linearly,
    /// intensity by reach² so surface brightness stays identical (point lights
    /// fall off with distance squared — see AddThreePointLights for the math).
    /// </summary>
    static void AddLights(Transform rig, float reach)
    {
        AddLight(rig, new Vector3(1.7f, 1.9f, 2.1f) * reach, new Color(1f, 0.97f, 0.9f), 11f * reach * reach, 6f * reach);
        AddLight(rig, new Vector3(-1.9f, 0.5f, 1.7f) * reach, new Color(0.55f, 0.72f, 1f), 4.5f * reach * reach, 6f * reach);
        AddLight(rig, new Vector3(0f, 1.4f, -2.3f) * reach, new Color(0.8f, 0.9f, 1f), 6f * reach * reach, 6f * reach);
    }

    static void AddLight(Transform parent, Vector3 localPosition, Color color, float intensity,
        float range)
    {
        var go = new GameObject("IconLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    /// <summary>Camera on +Z looking back at the diorama, exactly like the card rigs.</summary>
    static Camera AddCamera(Transform rig, RenderTexture rt, Vector3 pos, float pitch, float fov,
        float far)
    {
        var camGo = new GameObject("IconCam");
        camGo.transform.SetParent(rig, false);
        camGo.transform.localPosition = pos;
        camGo.transform.localRotation = Quaternion.Euler(pitch, 180f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = far;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RobotSelectMenu.PreviewBackdrop;
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        return cam;
    }
}

/// <summary>
/// Ties the icon rigs' life to the menu canvas it sits beside: rigs (and their
/// ten cameras) run only while the menu is actually on screen, and everything
/// is released if the menu is ever destroyed. Lives on the canvas GameObject.
/// Runtime-only (never scene-serialized), so it may live in this file.
/// </summary>
public class MenuIconRigSync : MonoBehaviour
{
    public GameObject rigRoot;
    public RenderTexture[] textures;
    public Camera[] cameras;

    void OnEnable()
    {
        if (rigRoot != null) rigRoot.SetActive(true);
    }

    void OnDisable()
    {
        if (rigRoot != null) rigRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (cameras != null)
            foreach (var cam in cameras)
                if (cam != null)
                    cam.targetTexture = null;
        if (textures != null)
            foreach (var rt in textures)
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
        if (rigRoot != null)
            Destroy(rigRoot);
    }
}
