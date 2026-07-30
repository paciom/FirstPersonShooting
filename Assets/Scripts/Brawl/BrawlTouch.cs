using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The tablet hands: movement and jump on the left thumb, PUNCH / KICK /
/// BLOCK / BLAST on the right. Buttons appear only once TouchControls has
/// seen a real touch (same activation rule as the FPS pads), and feed
/// static state that BrawlInput folds into the same Intent the keyboard
/// writes — the fighter never knows which hands are driving.
/// </summary>
public class BrawlTouch : MonoBehaviour
{
    public static float Move { get; private set; }
    public static bool BlockHeld { get; private set; }

    static bool _jumpEdge, _punchEdge, _kickEdge, _blastEdge;

    public static bool ConsumeJump() => Consume(ref _jumpEdge);
    public static bool ConsumePunch() => Consume(ref _punchEdge);
    public static bool ConsumeKick() => Consume(ref _kickEdge);
    public static bool ConsumeBlast() => Consume(ref _blastEdge);

    static bool Consume(ref bool edge)
    {
        bool was = edge;
        edge = false;
        return was;
    }

    GameObject _canvas;
    bool _leftHeld, _rightHeld;

    void Update()
    {
        if (_canvas == null && TouchControls.Active)
            Build();
        Move = (_rightHeld ? 1f : 0f) - (_leftHeld ? 1f : 0f);
    }

    void OnDestroy()
    {
        Move = 0f;
        BlockHeld = false;
        _jumpEdge = _punchEdge = _kickEdge = _blastEdge = false;
    }

    void Build()
    {
        _canvas = new GameObject("BrawlTouch");
        _canvas.transform.SetParent(transform, false);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();

        // Left thumb: walk and jump.
        HoldPad("◀", new Vector2(90f, 150f), held => _leftHeld = held);
        HoldPad("▶", new Vector2(240f, 150f), held => _rightHeld = held);
        TapPad("JUMP", new Vector2(165f, 300f), () => _jumpEdge = true);

        // Right thumb: the fight.
        TapPad("PUNCH", new Vector2(-250f, 300f), () => _punchEdge = true);
        TapPad("KICK", new Vector2(-95f, 300f), () => _kickEdge = true);
        HoldPad("BLOCK", new Vector2(-250f, 150f), held => BlockHeld = held);
        TapPad("BLAST", new Vector2(-95f, 150f), () => _blastEdge = true);
    }

    void TapPad(string label, Vector2 corner, System.Action onDown)
    {
        MakePad(label, corner, down => { if (down) onDown(); });
    }

    void HoldPad(string label, Vector2 corner, System.Action<bool> onHeld)
    {
        MakePad(label, corner, onHeld);
    }

    void MakePad(string label, Vector2 corner, System.Action<bool> onState)
    {
        var image = new GameObject($"Pad_{label}").AddComponent<Image>();
        image.transform.SetParent(_canvas.transform, false);
        image.color = new Color(0.10f, 0.22f, 0.32f, 0.55f);
        var rect = image.rectTransform;
        // corner.x >= 0 anchors left edge, negative anchors right edge.
        float ax = corner.x >= 0f ? 0f : 1f;
        rect.anchorMin = rect.anchorMax = new Vector2(ax, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(corner.x, corner.y);
        rect.sizeDelta = new Vector2(140f, 130f);

        var text = new GameObject("Label").AddComponent<Text>();
        text.transform.SetParent(image.transform, false);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = 30;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(1f, 1f, 1f, 0.85f);
        text.alignment = TextAnchor.MiddleCenter;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

        var trigger = image.gameObject.AddComponent<EventTrigger>();
        Add(trigger, EventTriggerType.PointerDown, () => onState(true));
        Add(trigger, EventTriggerType.PointerUp, () => onState(false));
        Add(trigger, EventTriggerType.PointerExit, () => onState(false));
    }

    static void Add(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }
}
