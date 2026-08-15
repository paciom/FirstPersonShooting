using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen in front of ADVENTURE: pick a mini adventure. Each card is one
/// self-contained side file — a short branching story off the season's main
/// episode — and the card says how big it is and how much of it the player has
/// already seen, because finding the other endings is the mode's replay.
///
/// Same shape as the other pre-match screens: built at runtime, returned as a
/// GameObject the mode controller owns and destroys, Escape steps back.
/// </summary>
public static class AdventureSelect
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color CardColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    const int Columns = 3;
    const float CardWidth = 520f;
    const float CardHeight = 306f;
    const float PitchX = 548f;
    const float PitchY = 336f;

    public static GameObject Build(GameModeController controller)
    {
        var canvasGo = new GameObject("AdventureSelect");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var backdrop = Panel(canvasGo.transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.94f));
        backdrop.sprite = null;
        Stretch(backdrop.rectTransform);

        MakeText(canvasGo.transform, "Title", "ADVENTURE", 72, HoloCyan,
            FontStyle.Bold, new Vector2(0f, 424f), new Vector2(1400f, 90f));

        MakeText(canvasGo.transform, "Subtitle",
            "SIDE  FILES  FROM  THE  SEASON   ·   YOUR  CHOICES  ·   TEXT  MODE", 28,
            new Color(1f, 1f, 1f, 0.6f), FontStyle.Normal, new Vector2(0f, 368f),
            new Vector2(1400f, 40f));

        var stories = AdventureStory.All();
        if (stories.Length == 0)
        {
            MakeText(canvasGo.transform, "Empty", "NO  ADVENTURES  INSTALLED", 34,
                new Color(1f, 1f, 1f, 0.5f), FontStyle.Bold, Vector2.zero,
                new Vector2(1200f, 60f));
        }

        int rows = Mathf.CeilToInt(stories.Length / (float)Columns);
        float top = (rows - 1) * PitchY * 0.5f + 40f;

        for (int i = 0; i < stories.Length; i++)
        {
            var story = stories[i];
            string storyId = story.id;
            int column = i % Columns;
            int row = i / Columns;

            // Short last rows centre themselves rather than hugging the left.
            int inRow = Mathf.Min(Columns, stories.Length - row * Columns);
            float x = (column - (inRow - 1) * 0.5f) * PitchX;
            float y = top - row * PitchY;

            MakeStoryCard(canvasGo.transform, story, new Vector2(x, y),
                () => controller.StartAdventure(storyId));
        }

        MakeText(canvasGo.transform, "Hint",
            "EVERY  PATH  ENDS  IN  TEN  STEPS  OR  FEWER   ·   SOME  CHOICES  END  IT  SOONER",
            22, new Color(1f, 1f, 1f, 0.42f), FontStyle.Normal, new Vector2(0f, -364f),
            new Vector2(1500f, 34f));

        MakeBackButton(canvasGo.transform, controller.CancelAdventureSelect);
        return canvasGo;
    }

    static void MakeStoryCard(Transform parent, AdventureStory story, Vector2 position,
        UnityEngine.Events.UnityAction onClick)
    {
        var card = Panel(parent, $"Adventure_{story.id}", CardColor);
        Place(card.rectTransform, position, new Vector2(CardWidth, CardHeight));

        var tagline = MakeText(card.transform, "Tagline", story.tagline, 20,
            new Color(1f, 1f, 1f, 0.5f), FontStyle.Bold, new Vector2(0f, CardHeight * 0.5f - 34f),
            new Vector2(CardWidth - 48f, 26f));
        tagline.alignment = TextAnchor.MiddleLeft;

        var title = MakeText(card.transform, "Title", story.title, 40, HoloCyan, FontStyle.Bold,
            new Vector2(0f, CardHeight * 0.5f - 76f), new Vector2(CardWidth - 48f, 48f));
        title.alignment = TextAnchor.MiddleLeft;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 20;
        title.resizeTextMaxSize = 40;

        var logline = MakeText(card.transform, "Logline", story.logline, 23,
            new Color(1f, 1f, 1f, 0.82f), FontStyle.Normal, new Vector2(0f, -6f),
            new Vector2(CardWidth - 48f, 130f));
        logline.alignment = TextAnchor.UpperLeft;
        logline.horizontalOverflow = HorizontalWrapMode.Wrap;

        int found = AdventureProgress.FoundCount(story);
        int endings = story.EndingCount;
        var stats = MakeText(card.transform, "Stats",
            $"{story.nodes.Length}  BEATS   ·   {endings}  ENDINGS   ·   {found}  FOUND", 20,
            found > 0 ? HoloCyan : new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold,
            new Vector2(0f, -CardHeight * 0.5f + 34f), new Vector2(CardWidth - 48f, 26f));
        stats.alignment = TextAnchor.MiddleLeft;

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.45f, 1.45f, 1.45f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
    }

    static void MakeBackButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var panel = Panel(parent, "Back", new Color(0.10f, 0.26f, 0.36f, 0.96f));
        Place(panel.rectTransform, new Vector2(0f, -444f), new Vector2(260f, 68f));

        var label = MakeText(panel.transform, "Label", "BACK", 28, Color.white, FontStyle.Bold,
            Vector2.zero, new Vector2(250f, 54f));

        var button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
    }

    static Text MakeText(Transform parent, string name, string content, int size, Color color,
        FontStyle style, Vector2 position, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Place(text.rectTransform, position, sizeDelta);
        return text;
    }

    static Image Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = MainMenu.RoundedTile();
        image.type = Image.Type.Sliced;
        return image;
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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
