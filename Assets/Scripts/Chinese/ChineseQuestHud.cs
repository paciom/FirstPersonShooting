using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Everything Chinese Quest draws on the glass: the character at the bottom
/// of the screen, the four answer cards, the score and shields, the verdict
/// banner and the results panel.
///
/// The cards are screen-space but WORLD-ANCHORED — each one rides above its
/// robot's head every frame, so "the card in the top-left corner" and "the
/// robot in the top-left corner" are the same answer without the player
/// having to be told. Anchoring beats fixed corners here because the robots
/// move: one charges across the stage, another is blown backward, and a card
/// that stayed in its corner would quietly stop belonging to anybody.
///
/// Chinese draws through ChineseFont, never Text.text directly, so a
/// character missing from the packed font says so in the console instead of
/// appearing as a blank rectangle.
/// </summary>
public class ChineseQuestHud : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color CardIdle = new Color(0.06f, 0.14f, 0.22f, 0.94f);
    static readonly Color CardRight = new Color(0.10f, 0.42f, 0.20f, 0.96f);
    static readonly Color CardWrong = new Color(0.45f, 0.11f, 0.14f, 0.96f);
    static readonly Color Good = new Color(0.4f, 1f, 0.6f);
    static readonly Color Bad = new Color(1f, 0.5f, 0.45f);

    const float CardWidth = 320f;
    const float CardHeight = 116f;

    /// <summary>Head clearance: the card floats this far above the robot's post.</summary>
    const float CardLift = 2.5f;

    /// <summary>The prompt panel's footprint, which no card may sit on top of.</summary>
    const float PromptHalfWidth = 330f;
    const float PromptTop = 330f;

    public System.Action<int> OnPicked;
    public System.Action OnPlayAgain;

    class Card
    {
        public RectTransform rect;
        public Image panel;
        public Image badge;
        public Text number;
        public Text english;
        public Text pinyin;
        public Button button;
        public Transform anchor;
        public float pulse;
    }

    Camera _camera;
    Canvas _canvas;
    Card[] _cards;

    Text _prompt;
    Text _promptPinyin;
    Text _ask;
    Text _score;
    Text _streak;
    Text _bannerBig;
    Text _bannerSmall;
    Image[] _shields;
    GameObject _results;
    Text _resultsBody;

    // Chinese Run scores in road rather than lives, so the shield row's corner
    // is handed over to a distance readout. One HUD, two modes: the answer
    // cards ride above their robots either way, which is why a ring of four
    // and a line of four across a road need no different code at all.
    Text _distanceLabel;
    bool _distanceMeter;
    int _shownDistance = -1;

    float _bannerTime;

    public static ChineseQuestHud Build(Transform parent, Camera camera, ChineseLexicon.Deck deck)
    {
        var go = new GameObject("ChineseQuestHud");
        go.transform.SetParent(parent, false);
        var hud = go.AddComponent<ChineseQuestHud>();
        hud._camera = camera;
        hud.Compose(deck);
        return hud;
    }

    // --------------------------------------------------------------- compose

    void Compose(ChineseLexicon.Deck deck)
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var root = canvasGo.transform;
        BuildPrompt(root);
        BuildScoreboard(root, deck);
        BuildBanner(root);
        BuildCards(root);
        BuildResults(root);
    }

    /// <summary>The bottom of the screen: the character being asked about.</summary>
    void BuildPrompt(Transform root)
    {
        var panel = Panel(root, "Prompt", new Color(0.03f, 0.08f, 0.13f, 0.88f));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 26f);
        rect.sizeDelta = new Vector2(PromptHalfWidth * 2f, 286f);

        _ask = ChineseFont.MakeText(panel.transform, "Ask", "WHAT  DOES  THIS  MEAN?", 22,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Bold);
        Place(_ask.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(600f, 30f));

        // The character itself, as big as the panel allows — this is the thing
        // the player is here to read.
        _prompt = ChineseFont.MakeText(panel.transform, "Hanzi", "", 132, Color.white, FontStyle.Bold);
        Place(_prompt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(620f, 168f));
        _prompt.resizeTextForBestFit = true;
        _prompt.resizeTextMinSize = 48;
        _prompt.resizeTextMaxSize = 148;

        // Blank until the round is answered — and so is the pronunciation.
        // Both the written sound and the spoken one are held back for the same
        // reason: every card carries pinyin, so either would let the player
        // match instead of read.
        _promptPinyin = ChineseFont.MakeText(panel.transform, "Pinyin", "", 40, HoloCyan, FontStyle.Bold);
        Place(_promptPinyin.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(620f, 50f));

    }

    void BuildScoreboard(Transform root, ChineseLexicon.Deck deck)
    {
        _score = ChineseFont.MakeText(root, "Score", "SCORE  0", 34, Color.white, FontStyle.Bold,
            TextAnchor.UpperLeft);
        Place(_score.rectTransform, new Vector2(0f, 1f), new Vector2(230f, -46f), new Vector2(420f, 44f));

        _streak = ChineseFont.MakeText(root, "Streak", "", 26, new Color(1f, 0.85f, 0.35f),
            FontStyle.Bold, TextAnchor.UpperLeft);
        Place(_streak.rectTransform, new Vector2(0f, 1f), new Vector2(230f, -88f), new Vector2(420f, 34f));

        // Set once and never touched again — the deck cannot change mid-run.
        var deckLabel = ChineseFont.MakeText(root, "Deck", $"{deck.title}   ·   {deck.hanzi}", 26,
            new Color(1f, 1f, 1f, 0.6f), FontStyle.Bold);
        Place(deckLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(700f, 36f));

        // Shields, top right: what a wrong answer costs.
        _shields = new Image[5];
        for (int i = 0; i < _shields.Length; i++)
        {
            var pip = Panel(root, $"Shield{i}", HoloCyan);
            Place(pip.rectTransform, new Vector2(1f, 1f),
                new Vector2(-52f - (_shields.Length - 1 - i) * 44f, -50f), new Vector2(32f, 40f));
            _shields[i] = pip;
        }
    }

    void BuildBanner(Transform root)
    {
        _bannerBig = ChineseFont.MakeText(root, "BannerBig", "", 96, Good, FontStyle.Bold);
        Place(_bannerBig.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 84f), new Vector2(1400f, 120f));

        _bannerSmall = ChineseFont.MakeText(root, "BannerSmall", "", 40, Color.white, FontStyle.Bold);
        Place(_bannerSmall.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(1400f, 56f));

        _bannerBig.enabled = false;
        _bannerSmall.enabled = false;
    }

    void BuildCards(Transform root)
    {
        _cards = new Card[ChineseQuest.Options];
        for (int i = 0; i < _cards.Length; i++)
        {
            int index = i;
            var panel = Panel(root, $"Card{i + 1}", CardIdle);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var badge = Panel(panel.transform, "Badge", HoloCyan);
            Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(46f, 46f));

            var number = ChineseFont.MakeText(badge.transform, "Number", (i + 1).ToString(), 30,
                new Color(0.02f, 0.09f, 0.15f), FontStyle.Bold);
            Place(number.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));

            var english = ChineseFont.MakeText(panel.transform, "English", "", 34, Color.white,
                FontStyle.Bold);
            Place(english.rectTransform, new Vector2(0.5f, 1f), new Vector2(32f, -38f),
                new Vector2(CardWidth - 100f, 44f));
            english.horizontalOverflow = HorizontalWrapMode.Wrap;
            english.resizeTextForBestFit = true;
            english.resizeTextMinSize = 18;
            english.resizeTextMaxSize = 36;

            var pinyin = ChineseFont.MakeText(panel.transform, "Pinyin", "", 28, HoloCyan,
                FontStyle.Bold);
            Place(pinyin.rectTransform, new Vector2(0.5f, 0f), new Vector2(32f, 30f),
                new Vector2(CardWidth - 100f, 36f));
            pinyin.resizeTextForBestFit = true;
            pinyin.resizeTextMinSize = 16;
            pinyin.resizeTextMaxSize = 30;

            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => OnPicked?.Invoke(index));

            _cards[i] = new Card
            {
                rect = rect, panel = panel, badge = badge, number = number,
                english = english, pinyin = pinyin, button = button,
            };
        }
    }

    void BuildResults(Transform root)
    {
        _results = new GameObject("Results");
        _results.transform.SetParent(root, false);
        var dim = _results.AddComponent<Image>();
        dim.color = new Color(0.01f, 0.03f, 0.06f, 0.86f);
        Stretch(dim.rectTransform);

        var panel = Panel(_results.transform, "Panel", new Color(0.05f, 0.12f, 0.19f, 0.98f));
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 520f));

        var title = ChineseFont.MakeText(panel.transform, "Title", "SHIELDS  DOWN", 62,
            HoloCyan, FontStyle.Bold);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(700f, 80f));

        _resultsBody = ChineseFont.MakeText(panel.transform, "Body", "", 34, Color.white,
            FontStyle.Bold);
        Place(_resultsBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f),
            new Vector2(700f, 220f));

        MakeButton(panel.transform, "Again", "PLAY  AGAIN", new Vector2(0.5f, 0.5f),
            new Vector2(-170f, -180f), new Vector2(280f, 74f), 30,
            () => OnPlayAgain?.Invoke());
        // Straight out through the mode controller, the same exit Escape takes
        // — a touch player has no Escape key.
        MakeButton(panel.transform, "Menu", "MENU", new Vector2(0.5f, 0.5f),
            new Vector2(170f, -180f), new Vector2(280f, 74f), 30,
            () => { if (GameModeController.Instance != null) GameModeController.Instance.EnterMenu(); });

        _results.SetActive(false);
    }

    // ----------------------------------------------------------- the round

    public void SetAnchor(int index, Transform anchor)
    {
        if (index >= 0 && index < _cards.Length)
            _cards[index].anchor = anchor;
    }

    public void ShowQuestion(ChineseLexicon.Word word, ChineseLexicon.Word[] options)
    {
        ChineseFont.Set(_prompt, word.hanzi);
        ChineseFont.Set(_promptPinyin, "");
        ChineseFont.Set(_ask, "WHAT  DOES  THIS  MEAN?");

        for (int i = 0; i < _cards.Length; i++)
        {
            var card = _cards[i];
            ChineseFont.Set(card.english, options[i].english.ToUpperInvariant());
            ChineseFont.Set(card.pinyin, options[i].pinyin);
            card.panel.color = CardIdle;
            card.badge.color = HoloCyan;
            card.pulse = 0f;
        }

        _bannerBig.enabled = false;
        _bannerSmall.enabled = false;
    }

    /// <summary>Cards accept clicks only while the question is open.</summary>
    public void SetCardsLive(bool live)
    {
        foreach (var card in _cards)
            card.button.interactable = live;
    }

    public void MarkChosen(int index, bool correct)
    {
        var card = _cards[index];
        card.panel.color = correct ? CardRight : CardWrong;
        card.badge.color = correct ? Good : Bad;
        card.pulse = 1f;
    }

    public void Correct(ChineseLexicon.Word word, int streak)
    {
        ChineseFont.Set(_promptPinyin, word.pinyin);
        Banner("对了！", $"{word.pinyin}   —   {word.english.ToUpperInvariant()}", Good);
        if (streak >= 3)
            ChineseFont.Set(_bannerSmall,
                $"{word.pinyin}   —   {word.english.ToUpperInvariant()}   ·   {streak} IN A ROW");
    }

    /// <summary>
    /// The teaching beat. The card that WAS right lights up green next to the
    /// red one the player picked, so the correction is a comparison rather
    /// than a scolding.
    /// </summary>
    public void Wrong(ChineseLexicon.Word word, int correctIndex)
    {
        ChineseFont.Set(_promptPinyin, word.pinyin);
        var card = _cards[correctIndex];
        card.panel.color = CardRight;
        card.badge.color = Good;
        card.pulse = 1f;
        Banner("再试一次", $"{word.hanzi}   =   {word.pinyin}   —   {word.english.ToUpperInvariant()}", Bad);
    }

    void Banner(string big, string small, Color color)
    {
        ChineseFont.Set(_bannerBig, big);
        ChineseFont.Set(_bannerSmall, small);
        _bannerBig.color = color;
        _bannerBig.enabled = true;
        _bannerSmall.enabled = true;
        _bannerTime = 1f;
        _bannerBig.transform.localScale = Vector3.one * 1.4f;
    }

    public void SetScore(int score, int streak)
    {
        ChineseFont.Set(_score, $"SCORE  {score}");
        ChineseFont.Set(_streak, streak >= 2 ? $"STREAK  ×{streak}" : "");
    }

    /// <summary>
    /// Swap the shield pips for a distance readout — the runner's currency.
    /// Called once, before the first question.
    /// </summary>
    public void UseDistanceMeter()
    {
        _distanceMeter = true;
        foreach (var pip in _shields)
            pip.enabled = false;

        _distanceLabel = ChineseFont.MakeText(_canvas.transform, "Distance", "", 34,
            Color.white, FontStyle.Bold, TextAnchor.UpperRight);
        Place(_distanceLabel.rectTransform, new Vector2(1f, 1f), new Vector2(-250f, -46f),
            new Vector2(460f, 44f));
    }

    /// <summary>
    /// Metres down the road, and the best ever reached. Guarded on the whole
    /// number because this is called every frame and the alternative is a
    /// fresh string, a layout rebuild and a mesh every one of them.
    /// </summary>
    public void SetDistance(int metres, int best)
    {
        if (_distanceLabel == null || metres == _shownDistance)
            return;
        _shownDistance = metres;
        ChineseFont.Set(_distanceLabel,
            best > 0 ? $"{metres} m    ·    BEST  {best} m" : $"{metres} m");
    }

    /// <summary>
    /// Hide the cards outright — for the beat between a gate being settled and
    /// the next one being planted, where they would otherwise hang off robots
    /// that have already burst into light.
    /// </summary>
    public void SetCardsShown(bool shown)
    {
        foreach (var card in _cards)
            card.panel.gameObject.SetActive(shown);
    }

    public void SetShields(int shields, int max)
    {
        if (_distanceMeter)
            return;
        for (int i = 0; i < _shields.Length; i++)
        {
            bool lit = i < shields;
            bool used = i < max;
            _shields[i].enabled = used;
            _shields[i].color = lit ? HoloCyan : new Color(1f, 1f, 1f, 0.14f);
        }
    }

    public void ShowResults(int score, int right, int asked, int bestStreak)
    {
        int percent = asked > 0 ? Mathf.RoundToInt(100f * right / asked) : 0;
        ChineseFont.Set(_resultsBody,
            $"SCORE   {score}\n\n{right}  OF  {asked}  RIGHT   ·   {percent}%\n\nBEST  STREAK   ×{bestStreak}");
        _results.SetActive(true);
        SetCardsLive(false);
    }

    public void HideResults()
    {
        _results.SetActive(false);
    }

    // -------------------------------------------------------------- per frame

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (_bannerTime > 0f)
        {
            _bannerTime -= dt;
            _bannerBig.transform.localScale = Vector3.Lerp(_bannerBig.transform.localScale,
                Vector3.one, 1f - Mathf.Exp(-10f * dt));
        }

        foreach (var card in _cards)
        {
            if (card.pulse > 0f)
            {
                card.pulse = Mathf.Max(0f, card.pulse - dt * 2.2f);
                card.rect.localScale = Vector3.one * (1f + card.pulse * 0.12f);
            }
            PositionCard(card);
        }
    }

    /// <summary>
    /// Park the card above its robot's head, then drag it back on screen if
    /// that put it off the edge — a robot standing at the frame's corner is
    /// exactly the case where the naive projection loses its label.
    /// </summary>
    void PositionCard(Card card)
    {
        if (card.anchor == null || _camera == null)
            return;

        Vector3 head = card.anchor.position + Vector3.up * CardLift;
        Vector3 screen = _camera.WorldToScreenPoint(head);
        // Behind the camera projects to a mirrored point in front of it.
        if (screen.z < 0f)
            screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);

        float scale = _canvas.scaleFactor;
        float halfW = CardWidth * 0.5f * scale;
        float halfH = CardHeight * 0.5f * scale;

        float x = Mathf.Clamp(screen.x, halfW + 8f, Screen.width - halfW - 8f);
        float y = Mathf.Clamp(screen.y, halfH + 8f, Screen.height - halfH - 8f);

        // The prompt owns the bottom-centre of the screen. A card that would
        // overlap it is lifted clear rather than allowed to cover the very
        // character the question is about.
        float promptReach = (PromptHalfWidth * scale) + halfW;
        if (Mathf.Abs(x - Screen.width * 0.5f) < promptReach)
            y = Mathf.Max(y, PromptTop * scale + halfH);

        card.rect.position = new Vector3(x, y, 0f);
    }

    // ---------------------------------------------------------------- helpers

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

    static void MakeButton(Transform parent, string name, string label, Vector2 anchor,
        Vector2 position, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var panel = Panel(parent, name, new Color(0.10f, 0.26f, 0.36f, 0.98f));
        Place(panel.rectTransform, anchor, position, size);

        var text = ChineseFont.MakeText(panel.transform, "Label", label, fontSize, Color.white,
            FontStyle.Bold);
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(14f, 14f));

        var button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
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
