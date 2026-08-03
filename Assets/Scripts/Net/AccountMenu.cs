using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The PILOT ACCOUNT screen: sign in with a name (or email) + password, or
/// create a new pilot. Email is optional on creation — a kid can register
/// with nothing but a made-up name. Runtime uGUI in the OnlineMenu style.
/// </summary>
public static class AccountMenu
{
    public static void Open(GameModeController controller, GameObject mainMenuCanvas)
    {
        mainMenuCanvas.SetActive(false);

        var canvasGo = new GameObject("AccountMenu");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var ui = canvasGo.AddComponent<AccountMenuUi>();
        ui.Init(mainMenuCanvas);
    }
}

/// <summary>
/// The main menu's corner label: SIGN IN, or the signed-in pilot's name.
/// Follows the account state through <see cref="AccountClient.Changed"/>.
/// </summary>
public class AccountBadge : MonoBehaviour
{
    public Text label;

    static readonly Color SignedInColor = new Color(0.4f, 1f, 0.6f);
    static readonly Color SignedOutColor = new Color(1f, 1f, 1f, 0.75f);

    void OnEnable()
    {
        AccountClient.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        AccountClient.Changed -= Refresh;
    }

    void Refresh()
    {
        if (label == null)
            return;
        var account = AccountClient.Instance;
        bool signedIn = account != null && account.SignedIn;
        label.text = signedIn
            ? "PILOT  ·  " + account.Username.ToUpperInvariant()
            : "SIGN  IN";
        label.color = signedIn ? SignedInColor : SignedOutColor;
    }
}

/// <summary>Builds the panel and swaps between its three views.</summary>
public class AccountMenuUi : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color ButtonColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);
    static readonly Color ButtonHover = new Color(0.10f, 0.30f, 0.42f, 1f);
    static readonly Color FieldColor = new Color(0.04f, 0.10f, 0.16f, 0.95f);
    static readonly Color ErrorColor = new Color(1f, 0.45f, 0.4f);
    static readonly Color OkColor = new Color(0.4f, 1f, 0.6f);

    GameObject _mainMenuCanvas;
    GameObject _signedOutRoot;
    GameObject _signedInRoot;

    // Sign-in / new-pilot tab state and its widgets.
    bool _creating;
    GameObject _tabSignIn, _tabNewPilot;
    InputField _nameField, _passwordField, _emailField;
    Text _nameLabel, _emailLabel, _actionLabel, _statusText;
    Button _actionButton;

    Text _whoText, _emailText;

    public void Init(GameObject mainMenuCanvas)
    {
        _mainMenuCanvas = mainMenuCanvas;
        AccountClient.Ensure();
        Build();
        AccountClient.Changed += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        AccountClient.Changed -= Refresh;
    }

    void Update()
    {
        bool typing =
            (_nameField != null && _nameField.isFocused) ||
            (_passwordField != null && _passwordField.isFocused) ||
            (_emailField != null && _emailField.isFocused);
        if (Input.GetKeyDown(KeyCode.Escape) && !typing)
            Back();
    }

    void Back()
    {
        if (_mainMenuCanvas != null)
            _mainMenuCanvas.SetActive(true);
        Destroy(gameObject);
    }

    /// <summary>Point the whole screen at the current signed-in state.</summary>
    void Refresh()
    {
        var account = AccountClient.Instance;
        bool signedIn = account != null && account.SignedIn;
        _signedOutRoot.SetActive(!signedIn);
        _signedInRoot.SetActive(signedIn);
        if (signedIn)
        {
            _whoText.text = "PILOT   " + account.Username.ToUpperInvariant();
            _emailText.text = account.Email.Length > 0 ? account.Email : "no email on file";
        }
        else
        {
            ApplyTab();
        }
    }

    /// <summary>Retune the shared widgets for SIGN IN vs NEW PILOT.</summary>
    void ApplyTab()
    {
        SetTabColors(_tabSignIn, !_creating);
        SetTabColors(_tabNewPilot, _creating);
        _nameLabel.text = _creating ? "PILOT  NAME" : "NAME  OR  EMAIL";
        _actionLabel.text = _creating ? "CREATE  ACCOUNT" : "SIGN  IN";
        _emailLabel.gameObject.SetActive(_creating);
        _emailField.gameObject.SetActive(_creating);
        _statusText.text = _creating
            ? "email is optional, it lets you sign in anywhere"
            : "";
        _statusText.color = new Color(1f, 1f, 1f, 0.5f);

        // A name typed on one tab is still right on the other; a half-typed
        // email in the shared name box is not worth preserving. Keep it
        // simple: leave the fields alone, only the chrome changes.
    }

    static void SetTabColors(GameObject tab, bool active)
    {
        var image = tab.GetComponent<Image>();
        image.color = active ? new Color(0.10f, 0.30f, 0.42f, 1f) : ButtonColor;
        tab.transform.Find("Label").GetComponent<Text>().color =
            active ? HoloCyan : new Color(1f, 1f, 1f, 0.55f);
    }

    void Submit()
    {
        var account = AccountClient.Ensure();
        if (account.Busy)
            return;
        string name = _nameField.text.Trim();
        string password = _passwordField.text;
        if (name.Length == 0 || password.Length == 0)
        {
            ShowStatus("type a name and a password", ErrorColor);
            return;
        }

        ShowStatus(_creating ? "creating your pilot..." : "signing in...",
            new Color(1f, 1f, 1f, 0.7f));
        _actionButton.interactable = false;

        void Done(bool ok, string message)
        {
            // The reply can outlive the screen (player backed out while the
            // request flew) — touching destroyed widgets would throw.
            if (this == null)
                return;
            _actionButton.interactable = true;
            if (ok)
                Refresh();          // flips to the signed-in view
            else
                ShowStatus(message, ErrorColor);
        }

        if (_creating)
            account.Register(name, password, _emailField.text.Trim(), Done);
        else
            account.Login(name, password, Done);
    }

    void ShowStatus(string message, Color color)
    {
        _statusText.text = message;
        _statusText.color = color;
    }

    // ------------------------------------------------------------- building

    void Build()
    {
        var backdrop = MakeImage(transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.88f));
        var backdropRect = backdrop.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        MakeText(transform, "Title", "PILOT  ACCOUNT", 64, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -130), new Vector2(900, 90));
        MakeText(transform, "Subtitle", "ONE  NAME  ACROSS  EVERY  BATTLE", 24,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -195), new Vector2(900, 36));

        BuildSignedOut();
        BuildSignedIn();

        MakeButton(transform, "BACK", -390, Back);
    }

    void BuildSignedOut()
    {
        _signedOutRoot = new GameObject("SignedOut");
        _signedOutRoot.transform.SetParent(transform, false);
        var rect = _signedOutRoot.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var root = _signedOutRoot.transform;

        _tabSignIn = MakeTab(root, "SIGN  IN", -160, () => { _creating = false; ApplyTab(); });
        _tabNewPilot = MakeTab(root, "NEW  PILOT", 160, () => { _creating = true; ApplyTab(); });

        _nameLabel = MakeText(root, "NameLabel", "NAME  OR  EMAIL", 18,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, 118), new Vector2(560, 24));
        _nameField = MakeField(root, "Name", "", 80, false);
        // Names allow letters/digits/_ and emails add @ . + -; everything
        // else (spaces included) is a typo either way.
        _nameField.onValueChanged.AddListener(v =>
        {
            var sb = new System.Text.StringBuilder(v.Length);
            foreach (char c in v)
                if (char.IsLetterOrDigit(c) || "_@.+-".IndexOf(c) >= 0)
                    sb.Append(c);
            string clean = sb.ToString();
            if (clean != v)
                _nameField.SetTextWithoutNotify(clean);
        });

        MakeText(root, "PasswordLabel", "PASSWORD", 18,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(560, 24));
        _passwordField = MakeField(root, "Password", "", -40, true);
        _passwordField.onEndEdit.AddListener(_ =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Submit();
        });

        _emailLabel = MakeText(root, "EmailLabel", "EMAIL  (OPTIONAL)", 18,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, -122), new Vector2(560, 24));
        _emailField = MakeField(root, "Email", "", -160, false);
        _emailField.contentType = InputField.ContentType.EmailAddress;

        var action = MakeButton(root, "SIGN  IN", -250, Submit);
        _actionButton = action.GetComponent<Button>();
        _actionLabel = action.transform.Find("Label").GetComponent<Text>();

        _statusText = MakeText(root, "Status", "", 24,
            new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -312), new Vector2(1000, 34));
    }

    void BuildSignedIn()
    {
        _signedInRoot = new GameObject("SignedIn");
        _signedInRoot.transform.SetParent(transform, false);
        var rect = _signedInRoot.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var root = _signedInRoot.transform;

        _whoText = MakeText(root, "Who", "", 52, OkColor, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 70));
        _emailText = MakeText(root, "Email", "", 24, new Color(1f, 1f, 1f, 0.5f),
            FontStyle.Normal, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1000, 34));

        MakeText(root, "Blurb", "your name rides with you on any device you sign in on", 22,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1100, 32));

        // Logout fires Changed, which lands back in Refresh -> ApplyTab and
        // rebuilds a clean signed-out view.
        MakeButton(root, "LOG  OUT", -130, () => AccountClient.Ensure().Logout());
    }

    // ------------------------------------------------- widget construction

    GameObject MakeTab(Transform parent, string label, float x,
        UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Tab_{label}", ButtonColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 205);
        rect.sizeDelta = new Vector2(300, 64);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        MakeText(image.transform, "Label", label, 26, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 50));
        return image.gameObject;
    }

    InputField MakeField(Transform parent, string name, string placeholderText,
        float y, bool password)
    {
        var box = MakeImage(parent, $"Field_{name}", FieldColor);
        var boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = new Vector2(0, y);
        boxRect.sizeDelta = new Vector2(560, 64);

        var underline = MakeImage(box.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.6f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(6, 0);
        underline.rectTransform.offsetMax = new Vector2(-6, 3);

        var text = MakeText(box.transform, "Text", "", 28, Color.white, FontStyle.Normal,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(530, 54));
        text.alignment = TextAnchor.MiddleLeft;
        var placeholder = MakeText(box.transform, "Placeholder", placeholderText, 28,
            new Color(1f, 1f, 1f, 0.2f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(530, 54));
        placeholder.alignment = TextAnchor.MiddleLeft;

        var field = box.gameObject.AddComponent<InputField>();
        field.targetGraphic = box;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = password ? 72 : 254;
        if (password)
            field.contentType = InputField.ContentType.Password;
        return field;
    }

    GameObject MakeButton(Transform parent, string label, float y,
        UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", ButtonColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(460, 84);

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

        MakeText(image.transform, "Label", label, 32, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 70));
        return image.gameObject;
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
