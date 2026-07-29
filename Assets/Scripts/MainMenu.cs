using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the main menu canvas at runtime: title + three mode buttons.
/// Kid-friendly: big readable buttons, bright holographic styling.
/// </summary>
public static class MainMenu
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color ButtonColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);
    static readonly Color ButtonHover = new Color(0.10f, 0.30f, 0.42f, 1f);

    const string DesktopHint =
        "WASD move   ·   Mouse aim   ·   LMB fire   ·   Space jump   ·   T transform   ·   Z scope";

    static Text _hint;
    static InputField _seedField;

    /// <summary>
    /// The MAP CODE the player typed for Commander, or null for "roll one".
    /// Codes are how a map is revisited: every battlefield prints its code
    /// in the overlay, and typing it here rebuilds that exact map.
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

        // Both AI modes route through the robot select screen first.
        MakeButton(canvasGo.transform, "AI  v  AI", 200, () => controller.OpenRobotSelect(GameMode.AIvAI));
        MakeButton(canvasGo.transform, "PLAYER  v  AI", 80, () => controller.OpenRobotSelect(GameMode.PlayerVsAI));
        MakeButton(canvasGo.transform, "ONLINE  PVP", -40, () => OnlineMenu.Open(controller, canvasGo));
        MakeButton(canvasGo.transform, "COMMANDER", -160, controller.StartCommander);
        MakeButton(canvasGo.transform, "COMMANDER:  AI  WAR", -280, controller.StartCommanderWar);
        MakeButton(canvasGo.transform, "ARENA  BUILDER", -400, controller.StartArenaPreview);

        _hint = MakeText(canvasGo.transform, "Hint", DesktopHint,
            20, new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(1200, 30));

        MakeSeedBox(canvasGo.transform);

        return canvasGo;
    }

    /// <summary>
    /// The Commander MAP CODE entry, tucked into the bottom-right corner:
    /// blank rolls a fresh battlefield, a code rebuilds a known one.
    /// </summary>
    static void MakeSeedBox(Transform parent)
    {
        MakeText(parent, "SeedLabel", "COMMANDER  MAP  CODE", 16,
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

    static void MakeButton(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", ButtonColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(460, 92);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        // Thin cyan underline for the holographic look.
        var underline = MakeImage(image.transform, "Underline", new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.8f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(8, 0);
        underline.rectTransform.offsetMax = new Vector2(-8, 3);

        MakeText(image.transform, "Label", label, 34, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 80));
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
