using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TEXT MODE for the mini adventures: one beat on screen at a time — its
/// still, hook, scene and cliffhanger — and two ways out of it. The whole mode
/// is this screen; the 3D world is switched off behind an opaque backdrop,
/// because the point of text mode is that it can be read.
///
/// The same graph will drive VIDEO mode later, and the file is already written
/// for it: every node carries a 4-6 shot storyboard timed to thirty seconds,
/// and the still shown here is that beat's key shot. Video mode swaps this
/// screen's paragraph for a player and keeps everything else — the graph walk,
/// the step cap, the ending bookkeeping — exactly as it is here.
/// </summary>
public class AdventureReader : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color HookGold = new Color(1f, 0.84f, 0.42f);
    static readonly Color CliffBlue = new Color(0.58f, 0.78f, 1f);
    static readonly Color Ink = new Color(0.014f, 0.04f, 0.072f, 1f);
    static readonly Color ChoiceBed = new Color(0.09f, 0.23f, 0.33f, 0.96f);
    static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);

    const float Column = 1320f;
    // The still is shown WHOLE. It was a 300px band with a centre crop, which
    // sliced the top and bottom off every frame — and the characters stand at
    // the bottom of the frame, so it cut them off at the waist. 16:9, uncropped,
    // and the text lays out underneath it.
    const float StillWidth = 700f;
    const float StillHeight = StillWidth * 9f / 16f;   // 394
    const float ReadTop = 424f;        // under the header rule
    const float ReadHeight = 700f;     // down to just above the choices

    GameModeController _owner;
    AdventureStory _story;
    AdventureNode _node;
    int _step;

    CanvasGroup _fade;
    Text _crumb, _counter, _title, _hook, _body, _cliff, _foot;
    RawImage _still;
    ScrollRect _scroll;
    RectTransform _content;
    Transform _choiceRow;
    readonly List<Button> _choiceButtons = new List<Button>();

    public static AdventureReader Begin(GameModeController owner, string storyId)
    {
        var go = new GameObject("AdventureReader");
        go.transform.SetParent(owner.transform, false);
        var reader = go.AddComponent<AdventureReader>();
        reader._owner = owner;
        reader._story = AdventureStory.Load(storyId);
        reader.Build();
        return reader;
    }

    void Build()
    {
        var canvasGo = new GameObject("AdventureUi");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        // Opaque, not tinted: the arena camera is still rendering behind this.
        var backdrop = Panel(canvasGo.transform, "Backdrop", Ink);
        backdrop.sprite = null;
        Stretch(backdrop.rectTransform);

        var vignette = Panel(canvasGo.transform, "Vignette", new Color(0f, 0f, 0f, 0.55f));
        vignette.sprite = MenuArt.Vignette();
        vignette.type = Image.Type.Simple;
        vignette.raycastTarget = false;
        Stretch(vignette.rectTransform);

        var page = new GameObject("Page").AddComponent<RectTransform>();
        page.SetParent(canvasGo.transform, false);
        Stretch(page);
        _fade = page.gameObject.AddComponent<CanvasGroup>();

        if (_story == null)
        {
            var oops = MakeText(page, "Missing", "NO ADVENTURE LOADED", 44, HoloCyan,
                FontStyle.Bold, new Vector2(0f, 0f), new Vector2(Column, 80f));
            oops.alignment = TextAnchor.MiddleCenter;
            return;
        }

        _crumb = MakeText(page, "Crumb", $"{_story.title}   ·   {_story.tagline}", 22,
            Muted, FontStyle.Bold, new Vector2(0f, 462f), new Vector2(Column, 30f));
        _crumb.alignment = TextAnchor.MiddleLeft;

        _counter = MakeText(page, "Counter", "", 22, HoloCyan, FontStyle.Bold,
            new Vector2(0f, 462f), new Vector2(Column, 30f));
        _counter.alignment = TextAnchor.MiddleRight;

        var rule = Panel(page, "Rule", new Color(1f, 1f, 1f, 0.14f));
        rule.sprite = null;
        Place(rule.rectTransform, new Vector2(0f, 438f), new Vector2(Column, 1f));

        // Everything above the choices scrolls. A beat is 650-850 words now —
        // three times what a fixed box could hold at a readable size — so the
        // still, the title, the hook, the scene and the cliffhanger all live
        // inside one scrolling column, and only the two choices stay pinned.
        var viewGo = new GameObject("Viewport");
        viewGo.transform.SetParent(page, false);
        // RectMask2D, NOT Mask. Mask clips through the stencil buffer and
        // discards any pixel whose graphic alpha is under ~0.001, so the
        // near-invisible Image this used to carry masked the ENTIRE column
        // away: image, title, hook, scene and cliffhanger all gone, while the
        // header, choices and footer — siblings outside the mask — still drew.
        // That is exactly what shipped to play.jah.cc. RectMask2D clips by
        // rectangle, needs no graphic and no stencil, and cannot fail this way.
        viewGo.AddComponent<RectMask2D>();
        // A fully transparent graphic still has to be here, but only so the
        // raycaster has something to hit: without it the mouse wheel never
        // reaches the ScrollRect. Alpha 0 is fine for raycasting.
        var viewImage = viewGo.AddComponent<Image>();
        viewImage.color = new Color(0f, 0f, 0f, 0f);
        viewImage.raycastTarget = true;
        var viewRect = viewGo.GetComponent<RectTransform>();
        Place(viewRect, new Vector2(0f, ReadTop - ReadHeight * 0.5f),
            new Vector2(Column + 40f, ReadHeight));

        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewGo.transform, false);
        _content = contentGo.AddComponent<RectTransform>();
        _content.anchorMin = new Vector2(0.5f, 1f);
        _content.anchorMax = new Vector2(0.5f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(Column, ReadHeight);

        _scroll = viewGo.AddComponent<ScrollRect>();
        _scroll.content = _content;
        _scroll.viewport = viewRect;
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 42f;

        var stillGo = new GameObject("Still");
        stillGo.transform.SetParent(_content, false);
        _still = stillGo.AddComponent<RawImage>();
        _still.raycastTarget = false;
        _still.uvRect = new Rect(0f, 0f, 1f, 1f);
        Top(_still.rectTransform, new Vector2(StillWidth, StillHeight));

        _title = MakeText(_content, "Title", "", 46, HoloCyan, FontStyle.Bold,
            Vector2.zero, new Vector2(Column, 52f));
        _title.alignment = TextAnchor.UpperLeft;

        _hook = MakeText(_content, "Hook", "", 27, HookGold, FontStyle.Bold,
            Vector2.zero, new Vector2(Column, 38f));
        _hook.alignment = TextAnchor.UpperLeft;
        _hook.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Fixed size, not best-fit: the column scrolls now, so text never has
        // to shrink to fit, and a beat is read at one comfortable size whatever
        // its length.
        _body = MakeText(_content, "Body", "", 24, new Color(1f, 1f, 1f, 0.93f),
            FontStyle.Normal, Vector2.zero, new Vector2(Column, 100f));
        _body.alignment = TextAnchor.UpperLeft;
        _body.horizontalOverflow = HorizontalWrapMode.Wrap;
        _body.lineSpacing = 1.16f;

        _cliff = MakeText(_content, "Cliff", "", 26, CliffBlue, FontStyle.Italic,
            Vector2.zero, new Vector2(Column, 40f));
        _cliff.alignment = TextAnchor.UpperLeft;
        _cliff.horizontalOverflow = HorizontalWrapMode.Wrap;

        var row = new GameObject("Choices").AddComponent<RectTransform>();
        row.SetParent(page, false);
        Place(row, new Vector2(0f, -410f), new Vector2(Column, 190f));
        _choiceRow = row;

        _foot = MakeText(page, "Foot", "", 20, new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0f, -516f), new Vector2(Column, 24f));

        Show(_story.StartNode, 1);
    }

    void Show(AdventureNode node, int step)
    {
        _node = node;
        _step = step;

        // Reset here rather than on the way out of a node: PLAY AGAIN jumps
        // straight from an ending to the first beat without passing through a
        // choice, and would otherwise carry the ending's colour with it.
        _title.color = HoloCyan;
        // A beat with no painting yet simply reads as text: the mode shipped
        // before the art did, and must survive an adventure that has none.
        var still = Resources.Load<Texture2D>($"Adventures/{_story.id}/{node.id}");
        _still.texture = still;
        _still.color = still != null ? Color.white : new Color(1f, 1f, 1f, 0f);

        _title.text = node.title;
        _hook.text = node.hook;
        _body.text = node.body;
        _cliff.text = node.cliff;

        foreach (Transform child in _choiceRow)
            Destroy(child.gameObject);
        _choiceButtons.Clear();

        if (node.IsEnding)
            ShowEnding(node);
        else
            ShowChoices(node);

        Layout();
        StopAllCoroutines();
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Stack the column and size it to its contents. Done in code rather than
    /// with a VerticalLayoutGroup because the still is a fixed 16:9 block among
    /// three variable-height paragraphs, and the content height has to be exact
    /// or the scroll either clips the last line or scrolls into empty space.
    /// </summary>
    void Layout()
    {
        float cursor = 0f;

        if (_still.texture != null)
        {
            Top(_still.rectTransform, new Vector2(StillWidth, StillHeight));
            _still.rectTransform.anchoredPosition = new Vector2(0f, -cursor);
            cursor += StillHeight + 26f;
        }

        cursor = Stack(_title, cursor, 8f);
        cursor = Stack(_hook, cursor, 20f);
        cursor = Stack(_body, cursor, 22f);
        cursor = Stack(_cliff, cursor, 8f);

        _content.sizeDelta = new Vector2(Column, Mathf.Max(cursor, ReadHeight));
        // Every beat starts at its own first line, never wherever the previous
        // one was scrolled to.
        if (_scroll != null)
            _scroll.verticalNormalizedPosition = 1f;
    }

    float Stack(Text text, float cursor, float gap)
    {
        var rect = text.rectTransform;
        Top(rect, new Vector2(Column, text.preferredHeight));
        rect.anchoredPosition = new Vector2(0f, -cursor);
        return cursor + text.preferredHeight + gap;
    }

    void ShowChoices(AdventureNode node)
    {
        _counter.text = $"STEP  {_step}  /  {_story.maxSteps}";
        _counter.color = HoloCyan;

        for (int i = 0; i < node.choices.Length; i++)
        {
            var choice = node.choices[i];
            string target = choice.to;
            _choiceButtons.Add(MakeChoice(_choiceRow, i + 1, choice.text,
                new Vector2(0f, 46f - i * 92f), new Vector2(Column, 84f),
                () => Walk(target)));
        }
        _foot.text = "SCROLL  TO  READ        ·        1  /  2  —  CHOOSE        ·        ESC  —  MENU";
    }

    void ShowEnding(AdventureNode node)
    {
        bool fresh = !AdventureProgress.Found(_story.id, node.id);
        AdventureProgress.Record(_story.id, node.id);
        Metrics.Track("adventure_end", ("story", _story.id), ("ending", node.id),
            ("steps", _step), ("kind", node.ending));

        var tint = node.ending == "good" ? new Color(0.42f, 0.94f, 0.55f)
                 : node.ending == "bad" ? new Color(1f, 0.46f, 0.46f)
                 : new Color(0.78f, 0.56f, 1f);
        _counter.text = node.canon
            ? $"ENDING  ·  {node.ending.ToUpperInvariant()}  ·  THIS  IS  HOW  THE  SHOW  TELLS  IT"
            : $"ENDING  ·  {node.ending.ToUpperInvariant()}";
        _counter.color = tint;
        _title.color = tint;

        MakeChoice(_choiceRow, 1, "PLAY  AGAIN", new Vector2(-336f, 46f),
            new Vector2(640f, 84f), () => Show(_story.StartNode, 1));
        MakeChoice(_choiceRow, 2, "OTHER  ADVENTURES", new Vector2(336f, 46f),
            new Vector2(640f, 84f), () => _owner.BackToAdventureSelect());

        int found = AdventureProgress.FoundCount(_story);
        _foot.text = $"REACHED  IN  {_step}  STEPS        ·        "
                   + $"{found}  /  {_story.EndingCount}  ENDINGS  FOUND"
                   + (fresh ? "        ·        NEW" : "");
    }

    void Walk(string nodeId)
    {
        var next = _story.Find(nodeId);
        if (next == null)
        {
            // A bad link is an authoring bug, not a player-facing one: say so
            // loudly in the log and leave the player where they are rather
            // than dropping them into a blank screen.
            Debug.LogError($"[Adventure] '{_story.id}' node '{_node.id}' links to missing " +
                           $"'{nodeId}'. Run Tools/adventure_doc.py.");
            return;
        }
        Show(next, _step + 1);
    }

    void Update()
    {
        if (_choiceButtons.Count == 0)
            return;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            _choiceButtons[0].onClick.Invoke();
        else if (_choiceButtons.Count > 1
                 && (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)))
            _choiceButtons[1].onClick.Invoke();
    }

    IEnumerator FadeIn()
    {
        // Short enough that it never delays a reader, long enough that a new
        // beat reads as a cut rather than a text swap.
        for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
        {
            _fade.alpha = Mathf.Clamp01(t / 0.18f);
            yield return null;
        }
        _fade.alpha = 1f;
    }

    public void Teardown()
    {
        StopAllCoroutines();
        Destroy(gameObject);
    }

    // -- the small uGUI kit, per the house pattern: every screen carries its
    // own copy rather than depending on another screen's private helpers ----

    Button MakeChoice(Transform parent, int index, string label, Vector2 at, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var panel = Panel(parent, $"Choice{index}", ChoiceBed);
        Place(panel.rectTransform, at, size);

        var key = MakeText(panel.transform, "Key", index.ToString(), 34, HoloCyan,
            FontStyle.Bold, new Vector2(-size.x * 0.5f + 40f, 0f), new Vector2(48f, 48f));
        key.alignment = TextAnchor.MiddleCenter;

        var text = MakeText(panel.transform, "Label", label, 27, Color.white, FontStyle.Bold,
            new Vector2(38f, 0f), new Vector2(size.x - 120f, size.y - 20f));
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;

        var button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.45f, 1.45f, 1.45f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        return button;
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

    /// <summary>Anchor a child to the top of the scrolling column, so stacking
    /// it is a matter of one downward cursor.</summary>
    static void Top(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
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
