using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TEXT MODE for the mini adventures: one node on screen at a time — hook,
/// beat, cliffhanger — and two ways out of it. The whole mode is this screen;
/// the 3D world is switched off behind an opaque backdrop, because the point
/// of text mode is that it can be read.
///
/// The same graph will drive VIDEO mode later: a node's shot line is the clip
/// brief, so video mode replaces this screen's paragraph with a 30-second
/// player and keeps everything else — the graph walk, the step cap, the ending
/// bookkeeping — exactly as it is here.
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

    GameModeController _owner;
    AdventureStory _story;
    AdventureNode _node;
    int _step;

    CanvasGroup _fade;
    Text _crumb, _counter, _title, _hook, _body, _cliff, _foot;
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

        _title = MakeText(page, "Title", "", 52, HoloCyan, FontStyle.Bold,
            new Vector2(0f, 382f), new Vector2(Column, 64f));
        _title.alignment = TextAnchor.MiddleLeft;

        _hook = MakeText(page, "Hook", "", 30, HookGold, FontStyle.Bold,
            new Vector2(0f, 306f), new Vector2(Column, 74f));
        _hook.alignment = TextAnchor.UpperLeft;
        _hook.horizontalOverflow = HorizontalWrapMode.Wrap;

        _body = MakeText(page, "Body", "", 30, new Color(1f, 1f, 1f, 0.93f), FontStyle.Normal,
            new Vector2(0f, 158f), new Vector2(Column, 216f));
        _body.alignment = TextAnchor.UpperLeft;
        _body.horizontalOverflow = HorizontalWrapMode.Wrap;
        _body.lineSpacing = 1.08f;

        _cliff = MakeText(page, "Cliff", "", 29, CliffBlue, FontStyle.Italic,
            new Vector2(0f, 4f), new Vector2(Column, 84f));
        _cliff.alignment = TextAnchor.UpperLeft;
        _cliff.horizontalOverflow = HorizontalWrapMode.Wrap;

        var row = new GameObject("Choices").AddComponent<RectTransform>();
        row.SetParent(page, false);
        Place(row, new Vector2(0f, -200f), new Vector2(Column, 260f));
        _choiceRow = row;

        _foot = MakeText(page, "Foot", "", 21, new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0f, -430f), new Vector2(Column, 28f));

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

        StopAllCoroutines();
        StartCoroutine(FadeIn());
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
                new Vector2(0f, 62f - i * 118f), new Vector2(Column, 104f),
                () => Walk(target)));
        }
        _foot.text = "1  /  2  —  CHOOSE        ·        ESC  —  MENU";
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

        MakeChoice(_choiceRow, 1, "PLAY  AGAIN", new Vector2(-336f, 62f),
            new Vector2(640f, 104f), () => Show(_story.StartNode, 1));
        MakeChoice(_choiceRow, 2, "OTHER  ADVENTURES", new Vector2(336f, 62f),
            new Vector2(640f, 104f), () => _owner.BackToAdventureSelect());

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
