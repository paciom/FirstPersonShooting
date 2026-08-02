using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen in front of both Chinese modes: pick which words to fight over.
///
/// The deck matters more here than a stage picker does in Brawl, because the
/// deck IS the difficulty. A round's three wrong answers are drawn from the
/// same deck as the right one, so ANIMALS asks you to tell four animals
/// apart while EVERYTHING asks you to tell a colour from a verb — the same
/// question, two different games.
///
/// Same shape as the other pre-match screens: built at runtime, returned as
/// a GameObject the mode controller owns and destroys, Escape steps back.
/// </summary>
public static class ChineseDeckSelect
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color CardColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);

    // Five across, because the deck list grew to thirteen and a four-wide
    // grid needed a fourth row that ran into the BACK button.
    const int Columns = 5;
    const float CardWidth = 300f;
    const float CardHeight = 168f;
    const float PitchX = 316f;
    const float PitchY = 198f;

    public static GameObject Build(GameModeController controller, GameMode mode)
    {
        bool running = mode == GameMode.ChineseRun;
        var canvasGo = new GameObject("ChineseDeckSelect");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var backdrop = Panel(canvasGo.transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.9f));
        backdrop.sprite = null;
        Stretch(backdrop.rectTransform);

        var title = ChineseFont.MakeText(canvasGo.transform, "Title",
            running ? "CHINESE  RUN" : "CHINESE  QUEST", 72, HoloCyan, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1200f, 90f));

        var subtitle = ChineseFont.MakeText(canvasGo.transform, "Subtitle",
            running ? "汉字快跑   ·   PICK  YOUR  WORDS" : "汉字大冒险   ·   PICK  YOUR  WORDS",
            28, new Color(1f, 1f, 1f, 0.6f));
        Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -142f),
            new Vector2(1200f, 40f));

        int count = ChineseLexicon.MenuCount;
        int rows = Mathf.CeilToInt(count / (float)Columns);
        // Centre the block under the title: rows are laid out downward from
        // here, so the whole grid stays vertically centred whatever its height.
        float top = (rows - 1) * PitchY * 0.5f - 40f;

        for (int i = 0; i < count; i++)
        {
            int deckIndex = i;
            var deck = ChineseLexicon.DeckAt(i);
            int column = i % Columns;
            int row = i / Columns;

            // The last row is usually short; centre it rather than leaving a
            // ragged gap on the right.
            int inRow = Mathf.Min(Columns, count - row * Columns);
            float x = (column - (inRow - 1) * 0.5f) * PitchX;
            float y = top - row * PitchY;

            MakeDeckCard(canvasGo.transform, deck, Subtitle(i, deck), new Vector2(x, y),
                running ? () => controller.StartChineseRun(deckIndex)
                        : (UnityEngine.Events.UnityAction)(() => controller.StartChineseQuest(deckIndex)));
        }

        var hint = ChineseFont.MakeText(canvasGo.transform, "Hint",
            running
                ? "ANSWER  BEFORE  YOU  REACH  THEM   ·   TAP  A  ROBOT  OR  A  CARD   ·   1 – 4"
                : "TAP  A  ROBOT  OR  A  CARD  TO  ANSWER   ·   1 – 4  ON  THE  KEYBOARD",
            22, new Color(1f, 1f, 1f, 0.42f));
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(1500f, 34f));

        // A real button, not only the Escape hint: every other select screen in
        // the game has one, and a tablet has no Escape key.
        MakeBackButton(canvasGo.transform, controller.CancelChineseDeckSelect);

        return canvasGo;
    }

    static void MakeDeckCard(Transform parent, ChineseLexicon.Deck deck, string subtitle,
        Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        var card = Panel(parent, $"Deck_{deck.title}", CardColor);
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(CardWidth, CardHeight));

        // Three characters off the top of the deck: the card shows what it is
        // rather than describing it.
        var sample = ChineseFont.MakeText(card.transform, "Sample", Sample(deck), 50,
            new Color(1f, 1f, 1f, 0.92f), FontStyle.Bold);
        Place(sample.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -48f),
            new Vector2(CardWidth - 24f, 62f));

        var title = ChineseFont.MakeText(card.transform, "Title", deck.title, 32, HoloCyan,
            FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 56f),
            new Vector2(CardWidth - 24f, 38f));
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 16;
        title.resizeTextMaxSize = 30;

        var chinese = ChineseFont.MakeText(card.transform, "Chinese", subtitle, 22,
            new Color(1f, 1f, 1f, 0.55f));
        Place(chinese.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            new Vector2(CardWidth - 18f, 30f));
        chinese.resizeTextForBestFit = true;
        chinese.resizeTextMinSize = 14;
        chinese.resizeTextMaxSize = 22;

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.45f, 1.45f, 1.45f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
    }

    /// <summary>
    /// The line under a deck's name. Themed decks say how many words they
    /// hold, which is all there is to know about them. The syllabus deck says
    /// how much of it has been RETIRED — that number only ever goes up, and it
    /// is the only score in this game that means the player learned something.
    /// </summary>
    static string Subtitle(int index, ChineseLexicon.Deck deck)
    {
        if (!ChineseLexicon.IsInfinite(index))
            return $"{deck.hanzi}   ·   {deck.Count} WORDS";
        int learned = new ChineseProgress(deck.Count).Learned;
        return $"{deck.hanzi}   ·   {learned} / {deck.Count} LEARNED";
    }

    /// <summary>
    /// Three single characters from the deck, so the sample line is a row of
    /// glyphs and not one long compound crushed to fit.
    /// </summary>
    static string Sample(ChineseLexicon.Deck deck)
    {
        var picked = "";
        int shown = 0;
        foreach (var word in deck.words)
        {
            if (word.hanzi.Length != 1)
                continue;
            picked += (shown > 0 ? "  " : "") + word.hanzi;
            if (++shown == 3)
                break;
        }
        // A deck of nothing but compounds (FAMILY is close) still gets a face.
        if (shown == 0 && deck.Count > 0)
            picked = deck.words[0].hanzi;
        return picked;
    }

    static void MakeBackButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var panel = Panel(parent, "Back", new Color(0.10f, 0.26f, 0.36f, 0.96f));
        Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 78f),
            new Vector2(260f, 68f));

        var label = ChineseFont.MakeText(panel.transform, "Label", "BACK", 28, Color.white,
            FontStyle.Bold);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 54f));

        var button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
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

    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
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
}
