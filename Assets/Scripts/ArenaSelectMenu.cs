using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The arena picker, shown between robot select and the match in both AI modes.
/// Built at runtime like every other screen in this project.
///
/// Each card previews the arena the only way it can before the arena exists:
/// its palette, as a swatch strip. Real overhead art comes later from
/// PreviewCaptureTool.
/// </summary>
public static class ArenaSelectMenu
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color CardColor = new Color(0.05f, 0.11f, 0.18f, 0.96f);
    static readonly Color CardHover = new Color(0.10f, 0.30f, 0.42f, 1f);

    public static GameObject Build(GameModeController controller, int currentIndex)
    {
        var canvasGo = new GameObject("ArenaSelect");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var backdrop = MakeImage(canvasGo.transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.88f));
        Stretch(backdrop.rectTransform);

        MakeText(canvasGo.transform, "Title", "CHOOSE  YOUR  ARENA", 62, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(1400, 80));
        MakeText(canvasGo.transform, "Subtitle", "Each one plays differently", 24,
            new Color(1f, 1f, 1f, 0.5f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(900, 36));

        var arenas = ArenaLibrary.All;
        // Same trick the robot cards use: shrink the spacing rather than scroll,
        // so the whole roster stays visible however long it gets.
        float spacing = Mathf.Min(340f, 1740f / Mathf.Max(1, arenas.Length));
        float cardWidth = Mathf.Min(300f, spacing - 20f);

        for (int i = 0; i < arenas.Length; i++)
        {
            float x = (i - (arenas.Length - 1) * 0.5f) * spacing;
            MakeCard(canvasGo.transform, arenas[i], i, x, cardWidth, i == currentIndex, controller);
        }

        MakeButton(canvasGo.transform, "BACK", new Vector2(0f, 90f), new Vector2(280, 70), 28,
            controller.CancelArenaSelect);

        MakeText(canvasGo.transform, "Hint", "ESC — back to robot select", 20,
            new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(900, 28));

        return canvasGo;
    }

    static void MakeCard(Transform parent, ArenaDefinition arena, int index, float x, float width,
                         bool isCurrent, GameModeController controller)
    {
        var card = MakeImage(parent, $"Arena_{arena.DisplayName}", CardColor);
        var rect = card.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 30f);
        rect.sizeDelta = new Vector2(width, 400f);

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        var colors = button.colors;
        colors.highlightedColor = CardHover;
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        int captured = index;
        button.onClick.AddListener(() => controller.LaunchArena(captured));

        var palette = arena.Palette;

        // Palette band across the top — the closest thing to a preview until
        // real overhead captures exist.
        var band = MakeImage(card.transform, "Band", palette.floor);
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.anchoredPosition = new Vector2(0f, -12f);
        band.rectTransform.sizeDelta = new Vector2(-24f, 120f);

        var swatches = new[] { palette.accentA, palette.accentB, palette.wall };
        for (int s = 0; s < swatches.Length; s++)
        {
            var swatch = MakeImage(band.transform, "Swatch", swatches[s]);
            swatch.rectTransform.anchorMin = new Vector2(0f, 0f);
            swatch.rectTransform.anchorMax = new Vector2(0f, 0f);
            swatch.rectTransform.pivot = new Vector2(0f, 0f);
            swatch.rectTransform.anchoredPosition = new Vector2(14f + s * 34f, 12f);
            swatch.rectTransform.sizeDelta = new Vector2(28f, 28f);
        }

        if (arena.Levels > 1)
        {
            var badge = MakeImage(band.transform, "Badge", new Color(0f, 0f, 0f, 0.65f));
            badge.rectTransform.anchorMin = new Vector2(1f, 1f);
            badge.rectTransform.anchorMax = new Vector2(1f, 1f);
            badge.rectTransform.pivot = new Vector2(1f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(-10f, -10f);
            badge.rectTransform.sizeDelta = new Vector2(96f, 30f);
            MakeText(badge.transform, "BadgeText", $"{arena.Levels} LEVELS", 17, HoloCyan,
                FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 28f));
        }

        MakeText(card.transform, "Name", arena.DisplayName, 32, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -152), new Vector2(width - 24f, 44f));

        var tagline = MakeText(card.transform, "Tagline", arena.Tagline, 19,
            new Color(1f, 1f, 1f, 0.62f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -250), new Vector2(width - 40f, 130f));
        tagline.alignment = TextAnchor.UpperCenter;

        if (isCurrent)
        {
            var marker = MakeImage(card.transform, "Current", HoloCyan);
            marker.rectTransform.anchorMin = new Vector2(0f, 0f);
            marker.rectTransform.anchorMax = new Vector2(1f, 0f);
            marker.rectTransform.offsetMin = new Vector2(8f, 0f);
            marker.rectTransform.offsetMax = new Vector2(-8f, 5f);
        }
    }

    static void MakeButton(Transform parent, string label, Vector2 position, Vector2 size, int fontSize,
                           UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", new Color(0.06f, 0.14f, 0.22f, 0.95f));
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = CardHover;
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        MakeText(image.transform, "Label", label, fontSize, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(20f, 10f));
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
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
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
}
