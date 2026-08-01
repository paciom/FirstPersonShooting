using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the main menu canvas at runtime: title plus an app-style icon grid —
/// ten rounded tiles, each showing a live-rendered frozen 3D diorama of its
/// mode (two robots mid-punch for Brawl, a base for Commander, and so on; see
/// MenuIconRigs). Kid-friendly: big pictures first, words second.
/// </summary>
public static class MainMenu
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color TileColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    // The icon grid: two rows on the 1920x1080 reference canvas, each row
    // centred on its own count so an odd number of modes does not leave a
    // ragged gap on the right. Six across is the widest row here — 6 x 250 =
    // 1500, comfortably inside 1920 even before the tiles are inset.
    //
    // Row 1's labels bottom out at -379, inside the -403 extent the old
    // ten-button list proved safe on ultrawide, where match-width scaling
    // shrinks the reference height and clips anything lower.
    const float IconSize = 190f;
    const float IconPitch = 250f;
    const float Row0Y = 50f;
    const float Row1Y = -225f;

    const string DesktopHint =
        "WASD move   ·   Mouse aim   ·   LMB fire   ·   Space jump   ·   T transform   ·   Z scope";

    static Text _hint;
    static InputField _seedField;
    static Sprite _roundedTile;

    /// <summary>
    /// The MAP CODE the player typed for Commander or Tower Defense, or null
    /// for "roll one". Codes are how a map is revisited: every battlefield
    /// prints its code in the overlay, and typing it here rebuilds that
    /// exact map — in whichever strategy mode is launched next.
    /// </summary>
    public static int? RequestedSeed
    {
        get
        {
            if (_seedField == null || string.IsNullOrWhiteSpace(_seedField.text))
                return null;
            return int.TryParse(_seedField.text.Trim(), out int seed) ? seed : (int?)null;
        }
    }

    public static GameObject Build(GameModeController controller)
    {
        var canvasGo = new GameObject("MainMenu");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        // Dimmed backdrop so the arena shimmers behind the menu.
        var backdrop = MakeImage(canvasGo.transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.82f));
        Stretch(backdrop.rectTransform);

        MakeText(canvasGo.transform, "Title", "PHOTON ARENA", 84, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(1200, 110));
        MakeText(canvasGo.transform, "Subtitle", "LASER TAG OF THE FUTURE", 26,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -225), new Vector2(800, 40));

        // The dioramas behind the icons. NOT parented under this canvas — a
        // ScreenSpaceOverlay canvas is scaled to screen pixels, which would
        // warp rig geometry against its fixed-range lights. A sync component
        // ties their on/off and cleanup to this canvas instead.
        var icons = MenuIconRigs.Build(controller.transform, controller.GetComponent<RobotRoster>());
        var sync = canvasGo.AddComponent<MenuIconRigSync>();
        sync.rigRoot = icons.root;
        sync.textures = icons.textures;
        sync.cameras = icons.cameras;

        Texture Icon(MenuIcon icon) => icons.textures[(int)icon];

        // Row one: the shooter, the whole Brawl family (all route through robot
        // select) and the online match — everything that is two robots fighting.
        // Row two: the strategy modes, the learning mode, the builder.
        MakeAppIcon(canvasGo.transform, "AI  v  AI", Icon(MenuIcon.AIvAI), 0, 6, 0,
            () => controller.OpenRobotSelect(GameMode.AIvAI));
        MakeAppIcon(canvasGo.transform, "PLAYER  v  AI", Icon(MenuIcon.PlayerVsAI), 1, 6, 0,
            () => controller.OpenRobotSelect(GameMode.PlayerVsAI));
        MakeAppIcon(canvasGo.transform, "BRAWL", Icon(MenuIcon.Brawl), 2, 6, 0,
            () => controller.OpenRobotSelect(GameMode.Brawl));
        MakeAppIcon(canvasGo.transform, "BRAWL:  AI  v  AI", Icon(MenuIcon.BrawlWar), 3, 6, 0,
            () => controller.OpenRobotSelect(GameMode.BrawlWar));
        MakeAppIcon(canvasGo.transform, "MARTIAL  ARTS  SHOW", Icon(MenuIcon.BrawlShow), 4, 6, 0,
            () => controller.OpenRobotSelect(GameMode.BrawlShow));
        MakeAppIcon(canvasGo.transform, "ONLINE  PVP", Icon(MenuIcon.OnlinePvP), 5, 6, 0,
            () => OnlineMenu.Open(controller, canvasGo));
        MakeAppIcon(canvasGo.transform, "COMMANDER", Icon(MenuIcon.Commander), 0, 6, 1,
            controller.StartCommander);
        MakeAppIcon(canvasGo.transform, "COMMANDER:  AI  WAR", Icon(MenuIcon.CommanderWar), 1, 6, 1,
            controller.StartCommanderWar);
        MakeAppIcon(canvasGo.transform, "TOWER  DEFENSE", Icon(MenuIcon.TowerDefense), 2, 6, 1,
            controller.StartTowerDefense);
        MakeAppIcon(canvasGo.transform, "CHINESE  QUEST", Icon(MenuIcon.ChineseQuest), 3, 6, 1,
            controller.OpenChineseDeckSelect);
        MakeAppIcon(canvasGo.transform, "CHINESE  RUN", Icon(MenuIcon.ChineseRun), 4, 6, 1,
            controller.OpenChineseRunDeckSelect);
        MakeAppIcon(canvasGo.transform, "ARENA  BUILDER", Icon(MenuIcon.ArenaBuilder), 5, 6, 1,
            controller.StartArenaPreview);

        _hint = MakeText(canvasGo.transform, "Hint", DesktopHint,
            20, new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(1200, 30));

        MakeSeedBox(canvasGo.transform);

        return canvasGo;
    }

    /// <summary>
    /// The MAP CODE entry, tucked into the bottom-right corner: blank rolls
    /// a fresh battlefield, a code rebuilds a known one. Read by Commander
    /// and Tower Defense both.
    /// </summary>
    static void MakeSeedBox(Transform parent)
    {
        MakeText(parent, "SeedLabel", "MAP  CODE", 16,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(1f, 0f), new Vector2(-118, 128), new Vector2(220, 22));

        var box = MakeImage(parent, "SeedBox", new Color(0.04f, 0.10f, 0.16f, 0.95f));
        var boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(1f, 0f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = new Vector2(-118, 92);
        boxRect.sizeDelta = new Vector2(200, 40);

        var underline = MakeImage(box.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.6f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(4, 0);
        underline.rectTransform.offsetMax = new Vector2(-4, 2);

        var text = MakeText(box.transform, "Text", "", 20, Color.white, FontStyle.Normal,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(184, 34));
        text.alignment = TextAnchor.MiddleCenter;

        var placeholder = MakeText(box.transform, "Placeholder", "random", 20,
            new Color(1f, 1f, 1f, 0.25f), FontStyle.Italic,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(184, 34));
        placeholder.alignment = TextAnchor.MiddleCenter;

        _seedField = box.gameObject.AddComponent<InputField>();
        _seedField.targetGraphic = box;
        _seedField.textComponent = text;
        _seedField.placeholder = placeholder;
        _seedField.characterLimit = 7;
        _seedField.contentType = InputField.ContentType.IntegerNumber;
    }

    /// <summary>
    /// Swap the control hint when the input scheme changes (TouchControls calls
    /// this). Null restores the keyboard wording.
    /// </summary>
    public static void SetHint(string hint)
    {
        if (_hint != null)
            _hint.text = string.IsNullOrEmpty(hint) ? DesktopHint : hint;
    }

    /// <summary>
    /// One app icon: a rounded tile filled edge-to-edge by its diorama render,
    /// with the mode name underneath — the phone-home-screen shape. The label
    /// is a SIBLING of the tile, not a child: the tile is a Mask, and a child
    /// label below the tile would be clipped to nothing.
    ///
    /// <paramref name="inRow"/> is how many icons share this row, so each row
    /// centres itself. Rows are rarely equal — modes get added one at a time.
    /// </summary>
    static void MakeAppIcon(Transform parent, string label, Texture preview, int column,
        int inRow, int row, UnityEngine.Events.UnityAction onClick)
    {
        float x = (column - (inRow - 1) * 0.5f) * IconPitch;
        float y = row == 0 ? Row0Y : Row1Y;

        var tile = MakeImage(parent, $"Icon_{label}", TileColor);
        tile.sprite = RoundedTile();
        tile.type = Image.Type.Sliced;
        var rect = tile.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(IconSize, IconSize);
        // The mask is what rounds the diorama's corners (its material runs
        // with UI alpha clip, so the sprite's soft corners cut the stencil).
        tile.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var previewGo = new GameObject("Preview");
        previewGo.transform.SetParent(tile.transform, false);
        var raw = previewGo.AddComponent<RawImage>();
        raw.texture = preview;
        Stretch(raw.rectTransform);

        // The Button tints the diorama itself — hover cools it toward holo
        // cyan, pressing dims it — since the tile backing is fully covered.
        var button = tile.gameObject.AddComponent<Button>();
        button.targetGraphic = raw;
        var colors = button.colors;
        colors.highlightedColor = new Color(0.72f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.35f, 0.7f, 0.8f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var text = MakeText(parent, $"Label_{label}", label, 19, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(x, y - IconSize * 0.5f - 33f),
            new Vector2(IconPitch - 10f, 52f));
        text.alignment = TextAnchor.UpperCenter;
    }

    /// <summary>
    /// The rounded-square sprite every tile shares, generated once: a signed-
    /// distance alpha ramp gives anti-aliased corners, and the 9-slice border
    /// keeps them circular at any tile size. No art asset needed.
    ///
    /// Public because it is the project's one rounded panel: any runtime
    /// screen that wants soft corners (Chinese Quest's answer cards, its
    /// results panel) should share this sprite rather than generate a second
    /// texture with a slightly different radius.
    /// </summary>
    public static Sprite RoundedTile()
    {
        if (_roundedTile != null)
            return _roundedTile;

        const int size = 64;
        const float radius = 16f;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            name = "MenuIconTile",
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance from the pixel to the inset core box = a rounded
                // rect's SDF; one-pixel ramp at the edge anti-aliases it.
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius);
                float cy = Mathf.Clamp(py, radius, size - radius);
                float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                byte a = (byte)(255f * Mathf.Clamp01(radius - d + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        _roundedTile = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        return _roundedTile;
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static Text MakeText(Transform parent, string name, string content, int size, Color color,
        FontStyle style, Vector2 anchor, Vector2 position, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}
