using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Brawl stage picker, shown between robot select and the fight —
/// the same step (and the same card idiom) the FPS modes use for arenas.
/// One card per stage: RANDOM first, then every FPS arena wearing its own
/// palette, then the three authored sets. Each card badges its toys, so a
/// kid knows which stage rains crates before committing.
/// </summary>
public static class BrawlStageSelect
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color CardColor = new Color(0.05f, 0.11f, 0.18f, 0.96f);
    static readonly Color CardHover = new Color(0.10f, 0.30f, 0.42f, 1f);

    public static GameObject Build(GameModeController controller, GameMode mode)
    {
        var canvasGo = new GameObject("BrawlStageSelect");
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

        MakeText(canvasGo.transform, "Title", "CHOOSE  YOUR  STAGE", 62, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(1400, 80));
        MakeText(canvasGo.transform, "Subtitle",
            mode == GameMode.BrawlWar ? "Where the exhibition happens" : "Where the brawl happens",
            24, new Color(1f, 1f, 1f, 0.5f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(900, 36));

        int selected = BrawlArenas.SelectedFor(mode);
        int count = BrawlArenas.CountWithRandom;
        float spacing = Mathf.Min(320f, 1760f / Mathf.Max(1, count));
        float cardWidth = Mathf.Min(280f, spacing - 16f);

        for (int selection = 0; selection < count; selection++)
        {
            float x = (selection - (count - 1) * 0.5f) * spacing;
            MakeCard(canvasGo.transform, controller, mode, selection, x, cardWidth,
                selection == selected);
        }

        MakeButton(canvasGo.transform, "BACK", new Vector2(0f, 90f), new Vector2(280, 70), 28,
            controller.CancelBrawlStageSelect);
        MakeText(canvasGo.transform, "Hint", "ESC — back to robot select", 20,
            new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(900, 28));

        return canvasGo;
    }

    static void MakeCard(Transform parent, GameModeController controller, GameMode mode,
        int selection, float x, float width, bool isCurrent)
    {
        string name;
        string tagline;
        Color bandColor;
        Color[] swatches;
        string toys = "";

        if (selection == 0)
        {
            name = "RANDOM";
            tagline = "A different arena every match";
            bandColor = new Color(0.08f, 0.10f, 0.16f);
            swatches = new[] { HoloCyan, new Color(1f, 0.25f, 0.9f), new Color(1f, 0.8f, 0.3f) };
        }
        else
        {
            var def = BrawlArenas.All[selection - 1];
            name = def.name;
            toys = Toys(def);
            if (def.remixArena)
            {
                var arena = ArenaLibrary.Get(def.arenaIndex);
                tagline = arena.Tagline;
                var palette = arena.Palette;
                bandColor = palette.floor;
                swatches = new[] { palette.accentA, palette.accentB, palette.wall };
            }
            else
            {
                tagline = def.name == "FRONTLINE" ? "The war's own doorstep"
                    : def.name == "CARRIER DECK" ? "Under a ship the size of the sky"
                    : "The corners bite";
                bandColor = def.name == "CRYSTAL QUARRY"
                    ? new Color(0.10f, 0.22f, 0.26f) : new Color(0.10f, 0.12f, 0.18f);
                swatches = new[] { HoloCyan, new Color(1f, 0.75f, 0.25f), new Color(0.45f, 0.9f, 1f) };
            }
        }

        var card = MakeImage(parent, $"Stage_{name}", CardColor);
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
        int captured = selection;
        button.onClick.AddListener(() => controller.ChooseBrawlStage(captured));

        var band = MakeImage(card.transform, "Band", bandColor);
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.anchoredPosition = new Vector2(0f, -12f);
        band.rectTransform.sizeDelta = new Vector2(-24f, 120f);

        if (selection == 0)
        {
            MakeText(band.transform, "Mystery", "?", 74, HoloCyan, FontStyle.Bold,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 110f));
        }
        for (int s = 0; s < swatches.Length; s++)
        {
            var swatch = MakeImage(band.transform, "Swatch", swatches[s]);
            swatch.rectTransform.anchorMin = new Vector2(0f, 0f);
            swatch.rectTransform.anchorMax = new Vector2(0f, 0f);
            swatch.rectTransform.pivot = new Vector2(0f, 0f);
            swatch.rectTransform.anchoredPosition = new Vector2(14f + s * 34f, 12f);
            swatch.rectTransform.sizeDelta = new Vector2(28f, 28f);
        }

        if (toys.Length > 0)
        {
            var badge = MakeImage(band.transform, "Badge", new Color(0f, 0f, 0f, 0.65f));
            badge.rectTransform.anchorMin = new Vector2(1f, 1f);
            badge.rectTransform.anchorMax = new Vector2(1f, 1f);
            badge.rectTransform.pivot = new Vector2(1f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(-10f, -10f);
            badge.rectTransform.sizeDelta = new Vector2(Mathf.Min(width - 30f, 150f), 30f);
            MakeText(badge.transform, "BadgeText", toys, 15, new Color(1f, 0.8f, 0.35f),
                FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(Mathf.Min(width - 34f, 146f), 28f));
        }

        MakeText(card.transform, "Name", name, 27, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -158), new Vector2(width - 20f, 60f));

        var line = MakeText(card.transform, "Tagline", tagline, 18,
            new Color(1f, 1f, 1f, 0.62f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -256), new Vector2(width - 36f, 120f));
        line.alignment = TextAnchor.UpperCenter;

        if (isCurrent)
        {
            var marker = MakeImage(card.transform, "Current", HoloCyan);
            marker.rectTransform.anchorMin = new Vector2(0f, 0f);
            marker.rectTransform.anchorMax = new Vector2(1f, 0f);
            marker.rectTransform.offsetMin = new Vector2(8f, 0f);
            marker.rectTransform.offsetMax = new Vector2(-8f, 5f);
        }
    }

    static string Toys(BrawlArenaDef def)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (def.crates) parts.Add("CRATES");
        if (def.balls) parts.Add("BALLS");
        if (def.lifts) parts.Add("LIFTS");
        if (def.crystalCorners) parts.Add("CRYSTALS");
        return string.Join(" · ", parts);
    }

    // ---------- uGUI helpers (same idiom as ArenaSelectMenu) ----------

    static void MakeButton(Transform parent, string label, Vector2 position, Vector2 size,
        int fontSize, UnityEngine.Events.UnityAction onClick)
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
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
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
