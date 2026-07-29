using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ONLINE PVP screen: host a match and read the code to a friend, or type
/// a friend's code and join. Runtime uGUI in the MainMenu style. Phase 1 ends
/// at a proven link (live RTT + direct/relay readout); starting the actual
/// match from here is phase 2.
/// </summary>
public static class OnlineMenu
{
    public static void Open(GameModeController controller, GameObject mainMenuCanvas)
    {
        mainMenuCanvas.SetActive(false);

        var canvasGo = new GameObject("OnlineMenu");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var ui = canvasGo.AddComponent<OnlineMenuUi>();
        ui.Init(mainMenuCanvas);
    }
}

/// <summary>Builds the panel and mirrors NetSession state into it each frame.</summary>
public class OnlineMenuUi : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color ButtonColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);
    static readonly Color ButtonHover = new Color(0.10f, 0.30f, 0.42f, 1f);

    GameObject _mainMenuCanvas;
    Text _codeText;
    Text _statusText;
    InputField _joinField;

    public void Init(GameObject mainMenuCanvas)
    {
        _mainMenuCanvas = mainMenuCanvas;
        Build();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Back();
            return;
        }

        var session = NetSession.Instance;
        if (session == null)
            return;

        _codeText.text = session.IsHost && session.MatchCode.Length > 0
            ? "MATCH  CODE:   " + session.MatchCode
            : "";
        _statusText.text = session.StatusLine;
        _statusText.color = session.Status == NetStatus.Failed
            ? new Color(1f, 0.45f, 0.4f)
            : session.Status == NetStatus.Connected
                ? new Color(0.4f, 1f, 0.6f)
                : new Color(1f, 1f, 1f, 0.7f);
    }

    void Back()
    {
        if (NetSession.Instance != null)
            NetSession.Instance.Disconnect();
        if (_mainMenuCanvas != null)
            _mainMenuCanvas.SetActive(true);
        Destroy(gameObject);
    }

    void Build()
    {
        var backdrop = MakeImage(transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.88f));
        var backdropRect = backdrop.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        MakeText(transform, "Title", "ONLINE  PVP", 64, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -130), new Vector2(900, 90));
        MakeText(transform, "Subtitle", "CHALLENGE  A  FRIEND  ANYWHERE", 24,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -195), new Vector2(900, 36));

        MakeButton(transform, "HOST  A  MATCH", 120, () => NetSession.Ensure().Host());

        _codeText = MakeText(transform, "Code", "", 44, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(900, 60));

        MakeText(transform, "JoinLabel", "OR  TYPE  A  FRIEND'S  CODE", 20,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(600, 30));
        BuildJoinRow();

        _statusText = MakeText(transform, "Status", "", 26,
            new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -210), new Vector2(1000, 40));

        if (!NetBridge.Available)
            _statusText.text = "online play runs in the web build — this screen is a preview here";

        MakeButton(transform, "BACK", -320, Back);
    }

    void BuildJoinRow()
    {
        // Code box, JOIN button beside it — one row, centered together.
        var box = MakeImage(transform, "JoinBox", new Color(0.04f, 0.10f, 0.16f, 0.95f));
        var boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = new Vector2(-110, -110);
        boxRect.sizeDelta = new Vector2(280, 70);

        var underline = MakeImage(box.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.6f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(6, 0);
        underline.rectTransform.offsetMax = new Vector2(-6, 3);

        var text = MakeText(box.transform, "Text", "", 34, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 60));
        var placeholder = MakeText(box.transform, "Placeholder", "CODE", 34,
            new Color(1f, 1f, 1f, 0.2f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 60));

        _joinField = box.gameObject.AddComponent<InputField>();
        _joinField.targetGraphic = box;
        _joinField.textComponent = text;
        _joinField.placeholder = placeholder;
        _joinField.characterLimit = 5;
        _joinField.contentType = InputField.ContentType.Alphanumeric;
        _joinField.onValueChanged.AddListener(v =>
        {
            string clean = NetSession.SanitizeCode(v);
            if (clean != v)
                _joinField.SetTextWithoutNotify(clean);
        });

        var joinImage = MakeImage(transform, "Button_JOIN", ButtonColor);
        var joinRect = joinImage.rectTransform;
        joinRect.anchorMin = joinRect.anchorMax = new Vector2(0.5f, 0.5f);
        joinRect.anchoredPosition = new Vector2(140, -110);
        joinRect.sizeDelta = new Vector2(180, 70);
        var joinButton = joinImage.gameObject.AddComponent<Button>();
        joinButton.targetGraphic = joinImage;
        var colors = joinButton.colors;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = HoloCyan * 0.6f;
        joinButton.colors = colors;
        joinButton.onClick.AddListener(() => NetSession.Ensure().Join(_joinField.text));
        MakeText(joinImage.transform, "Label", "JOIN", 30, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 60));
    }

    void MakeButton(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
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

        var underline = MakeImage(image.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.8f));
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
}
