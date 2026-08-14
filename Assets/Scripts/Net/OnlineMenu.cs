using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ONLINE PVP screen: a lobby of open arenas. Press QUICK MATCH and the
/// server pairs you with whoever has waited longest, or read the list and click
/// the arena you fancy. Hosting puts your own arena on that list for somebody
/// else to click. Match codes still work for playing a specific friend.
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
        // Balance width and height so ultrawide viewports don't shrink the
        // reference height and push the bottom buttons off screen.
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // A stale Failed session from a previous visit would flash its old
        // error into the fresh screen.
        if (NetSession.Instance != null && NetSession.Instance.Status == NetStatus.Failed)
            NetSession.Instance.Disconnect();

        // Before the link can possibly exist, so no match message can arrive
        // unsubscribed. NetSession drains the reliable channel in the same
        // call that flips it to Connected — a NetMatch created on noticing
        // "Connected" is already too late, and the host's proposal would be
        // dropped into the void with no retry on either side.
        NetMatch.Ensure();

        var ui = canvasGo.AddComponent<OnlineMenuUi>();
        ui.Init(mainMenuCanvas);
    }
}

/// <summary>Builds the lobby and mirrors NetSession state into it each frame.</summary>
public class OnlineMenuUi : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color ButtonColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);
    static readonly Color ButtonHover = new Color(0.10f, 0.30f, 0.42f, 1f);
    static readonly Color RowColor = new Color(0.04f, 0.11f, 0.18f, 0.92f);

    /// <summary>Rows on screen. Must match the server's LIST_PAGE_SIZE.</summary>
    const int PageSize = 5;

    /// <summary>
    /// How often the open list is re-read. Fast enough that a room appearing
    /// feels live, slow enough that a lobby full of idle browsers is nothing —
    /// each poll is one small frame on a socket that is already open.
    /// </summary>
    const float RefreshSeconds = 2f;

    GameObject _mainMenuCanvas;
    Text _codeText;
    Text _statusText;
    InputField _joinField;
    GameObject _startButton;
    string _notice = "";

    Text _lobbyHeader;
    Text _pageLabel;
    Text _hostLabel;
    readonly GameObject[] _rows = new GameObject[PageSize];
    readonly Text[] _rowArena = new Text[PageSize];
    readonly Text[] _rowHost = new Text[PageSize];
    readonly Text[] _rowAge = new Text[PageSize];
    readonly string[] _rowCodes = new string[PageSize];
    float _nextRefresh;

    /// <summary>Controls that belong to the pre-room state, hidden once in a room.</summary>
    readonly System.Collections.Generic.List<GameObject> _preRoomUi =
        new System.Collections.Generic.List<GameObject>();

    public void Init(GameObject mainMenuCanvas)
    {
        _mainMenuCanvas = mainMenuCanvas;
        // Before Build: it paints the rows once from whatever the session
        // already holds, and with no session yet that first paint is skipped
        // and the header sits on "loading" until the first poll lands.
        var session = NetSession.Ensure();
        Build();

        session.RoomsUpdated += FillRows;
        // Sign on immediately: the list is the screen, and a lobby that only
        // starts loading when you press something is a lobby that always looks
        // empty on arrival.
        session.Browse();
    }

    void OnDestroy()
    {
        if (NetSession.Instance != null)
            NetSession.Instance.RoomsUpdated -= FillRows;
    }

    void Update()
    {
        // The match taking over is this screen's success exit: the arena is
        // live behind us, the main menu stays hidden, the link stays up.
        var controller = GameModeController.Instance;
        if (controller != null && controller.Mode == GameMode.OnlinePvP)
        {
            Destroy(gameObject);
            return;
        }

        // Escape while typing a code just leaves the text box (the
        // InputField's own behavior) — it shouldn't also close the screen.
        if (Input.GetKeyDown(KeyCode.Escape)
            && (_joinField == null || !_joinField.isFocused))
        {
            Back();
            return;
        }

        var session = NetSession.Instance;
        if (session == null)
            return;

        // "In a room" rather than "linked": a player waiting on their own
        // hosted match has already chosen, and leaving the list of other
        // people's arenas under them invites a second choice they can't make.
        bool inRoom = session.Status == NetStatus.Hosting
            || session.Status == NetStatus.LinkingUp
            || session.Status == NetStatus.Connected;
        bool linked = session.Status == NetStatus.Connected;
        var match = NetMatch.Instance;

        foreach (var go in _preRoomUi)
            if (go != null && go.activeSelf == inRoom)
                go.SetActive(!inRoom);

        if (!inRoom && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            session.RequestRooms(session.RoomPage);
        }

        UpdateHostLabel();

        bool showStart = linked && session.IsHost
            && match != null && match.State == NetMatch.MatchState.Idle;
        if (_startButton.activeSelf != showStart)
            _startButton.SetActive(showStart);

        if (!linked)
            _codeText.text = session.Status == NetStatus.Hosting && session.MatchCode.Length > 0
                ? "YOUR  MATCH  CODE:   " + session.MatchCode
                : "";
        // Kept short and em-dash free: this line is 44pt in a 900-wide rect,
        // and LegacyRuntime.ttf has no glyph for "—" (it renders as a gap).
        else if (session.IsHost)
            _codeText.text = match != null && match.State != NetMatch.MatchState.Idle
                ? "STARTING  MATCH..."
                : "LINKED  ·  READY  TO  START";
        else
            _codeText.text = "WAITING  FOR  THE  HOST...";

        // A match that ended (peer left, link dropped, handshake timed out)
        // says why, and keeps saying it until something else happens — landing
        // silently back on this screen reads as a crash. A lobby miss (the
        // room filled up while you were reading) is the same idea, one notch
        // quieter: the list is still live underneath it.
        if (match != null)
        {
            string notice = match.ConsumeNotice();
            if (!string.IsNullOrEmpty(notice))
                _notice = notice;
        }
        string lobbyNotice = session.ConsumeLobbyNotice();
        if (!string.IsNullOrEmpty(lobbyNotice))
            _notice = lobbyNotice;

        if (!string.IsNullOrEmpty(_notice) && session.Status != NetStatus.Connected)
        {
            _statusText.text = _notice;
            _statusText.color = new Color(1f, 0.8f, 0.35f);
            return;
        }

        _statusText.text = session.StatusLine;
        _statusText.color = session.Status == NetStatus.Failed
            ? new Color(1f, 0.45f, 0.4f)
            : session.Status == NetStatus.Connected
                ? new Color(0.4f, 1f, 0.6f)
                : new Color(1f, 1f, 1f, 0.7f);
    }

    // ---------- the list ----------

    /// <summary>
    /// Repaint the five rows from the page the session is holding. Rows are
    /// built once and refilled rather than rebuilt: this runs every couple of
    /// seconds forever while the screen is open, and destroying and recreating
    /// twenty UI objects on that cadence is a garbage generator with nothing to
    /// show for it.
    /// </summary>
    void FillRows()
    {
        var session = NetSession.Instance;
        if (session == null)
            return;

        var rooms = session.Rooms;
        for (int i = 0; i < PageSize; i++)
        {
            bool used = rooms != null && i < rooms.Length && rooms[i] != null;
            if (_rows[i].activeSelf != used)
                _rows[i].SetActive(used);
            if (!used)
            {
                _rowCodes[i] = "";
                continue;
            }
            var room = rooms[i];
            _rowCodes[i] = room.code;
            var arena = ArenaLibrary.Get(room.arena);
            _rowArena[i].text = arena != null ? arena.DisplayName.ToUpperInvariant() : "ARENA";
            _rowHost[i].text = room.name;
            _rowAge[i].text = "waiting " + Age(room.age);
        }

        _pageLabel.text = "PAGE  " + (session.RoomPage + 1) + " / " + session.RoomPages;
        _lobbyHeader.text = session.RoomTotal > 0
            ? "OPEN  ARENAS  ·  LONGEST  WAIT  FIRST"
            : "NO  OPEN  ARENAS  YET";
    }

    static string Age(int seconds)
    {
        if (seconds < 60)
            return seconds + "s";
        return (seconds / 60) + "m " + (seconds % 60).ToString("00") + "s";
    }

    void JoinRow(int index)
    {
        string code = _rowCodes[index];
        if (!string.IsNullOrEmpty(code))
            NetSession.Ensure().Join(code);
    }

    void Page(int step)
    {
        var session = NetSession.Instance;
        if (session == null)
            return;
        int page = Mathf.Clamp(session.RoomPage + step, 0, session.RoomPages - 1);
        session.RequestRooms(page);
        // Straight after an explicit page turn, so the poll timer can't land a
        // stale page on top of the one just asked for.
        _nextRefresh = Time.unscaledTime + RefreshSeconds;
    }

    /// <summary>The arena a host would open, named on the button so it isn't a surprise.</summary>
    static int CurrentArena()
    {
        var gmc = GameModeController.Instance;
        return gmc != null ? gmc.CurrentArenaIndex : 0;
    }

    void UpdateHostLabel()
    {
        if (_hostLabel == null)
            return;
        var arena = ArenaLibrary.Get(CurrentArena());
        string wanted = arena != null
            ? "HOST  IN  " + arena.DisplayName.ToUpperInvariant()
            : "HOST  A  MATCH";
        if (_hostLabel.text != wanted)
            _hostLabel.text = wanted;
    }

    void Back()
    {
        if (NetSession.Instance != null)
            NetSession.Instance.Disconnect();
        if (_mainMenuCanvas != null)
            _mainMenuCanvas.SetActive(true);
        Destroy(gameObject);
    }

    // ---------- construction ----------

    void Build()
    {
        var backdrop = MakeImage(transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.88f));
        var backdropRect = backdrop.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        MakeText(transform, "Title", "ONLINE  PVP", 58, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(900, 80));
        MakeText(transform, "Subtitle", "PICK  AN  ARENA  AND  JUMP  IN", 22,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -146), new Vector2(900, 32));

        // The two ways in, side by side: let the server choose, or choose yourself.
        _preRoomUi.Add(MakeButton(transform, "QUICK  MATCH", new Vector2(-236, 310),
            new Vector2(452, 84), () =>
                NetSession.Ensure().QuickMatch(CurrentArena(), AccountClient.DisplayName)));
        var host = MakeButton(transform, "HOST  A  MATCH", new Vector2(236, 310),
            new Vector2(452, 84), () =>
                NetSession.Ensure().Host(CurrentArena(), AccountClient.DisplayName));
        _hostLabel = host.transform.Find("Label").GetComponent<Text>();
        _preRoomUi.Add(host);

        BuildLobby();

        _codeText = MakeText(transform, "Code", "", 40, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1000, 56));

        // The success path: appears for the host once the link is up.
        _startButton = MakeButton(transform, "START  MATCH", new Vector2(0, -30),
            new Vector2(460, 92), () => NetMatch.Ensure().ProposeMatch());
        _startButton.SetActive(false);

        BuildJoinRow();

        _statusText = MakeText(transform, "Status", "", 24,
            new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -350), new Vector2(1200, 36));

        if (!NetBridge.Available)
            _statusText.text = "online play runs in the web build — this screen is a preview here";

        MakeButton(transform, "BACK", new Vector2(0, -430), new Vector2(320, 74), Back);
    }

    /// <summary>
    /// Header, five arena rows and the page bar, all under one parent so the
    /// whole list shows and hides as a unit.
    /// </summary>
    void BuildLobby()
    {
        var go = new GameObject("Lobby");
        go.transform.SetParent(transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        _preRoomUi.Add(go);

        _lobbyHeader = MakeText(rect, "Header", "LOADING  ARENAS...", 20,
            new Color(1f, 1f, 1f, 0.45f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, 236), new Vector2(1000, 28));

        const float pitch = 74f, top = 178f;
        for (int i = 0; i < PageSize; i++)
            _rows[i] = BuildRow(rect, i, top - pitch * i);

        BuildPageBar(rect, -190f);
        FillRows();
    }

    /// <summary>
    /// One clickable arena. Three columns rather than one line of text: the
    /// arena is what you are choosing, the name is who you would be playing,
    /// and the wait is the reason to take this one over the next — they scan
    /// far better in fixed positions than run together.
    /// </summary>
    GameObject BuildRow(RectTransform parent, int index, float y)
    {
        var image = MakeImage(parent, "Row" + index, RowColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(1000, 64);

        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = HoloCyan * 0.6f;
        button.colors = colors;
        // The captured index, not the code: the row outlives every page it
        // shows, and a listener holding a code would join whatever room was
        // there when the screen was built.
        button.onClick.AddListener(() => JoinRow(index));

        var edge = MakeImage(image.transform, "Edge",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.5f));
        edge.rectTransform.anchorMin = new Vector2(0, 0);
        edge.rectTransform.anchorMax = new Vector2(0, 1);
        edge.rectTransform.offsetMin = new Vector2(0, 6);
        edge.rectTransform.offsetMax = new Vector2(5, -6);

        // Fixed columns that cannot collide: the arena gets the left 420, the
        // host name the middle, the wait the right. A long arena name growing
        // into the name beside it is the one thing a five-row list cannot
        // absorb, so the widths are budgeted rather than fitted.
        _rowArena[index] = MakeText(image.rectTransform, "Arena", "", 30, Color.white,
            FontStyle.Bold, new Vector2(0f, 0.5f), new Vector2(246, 0), new Vector2(420, 44));
        _rowArena[index].alignment = TextAnchor.MiddleLeft;
        _rowArena[index].horizontalOverflow = HorizontalWrapMode.Overflow;

        _rowHost[index] = MakeText(image.rectTransform, "Host", "", 24,
            new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(120, 0), new Vector2(300, 40));
        _rowHost[index].alignment = TextAnchor.MiddleLeft;

        _rowAge[index] = MakeText(image.rectTransform, "Age", "", 20,
            new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(1f, 0.5f), new Vector2(-220, 0), new Vector2(240, 36));
        _rowAge[index].alignment = TextAnchor.MiddleRight;

        MakeText(image.rectTransform, "Go", "JOIN", 26, HoloCyan, FontStyle.Bold,
            new Vector2(1f, 0.5f), new Vector2(-70, 0), new Vector2(120, 40));

        image.gameObject.SetActive(false);
        return image.gameObject;
    }

    void BuildPageBar(RectTransform parent, float y)
    {
        MakeButton(parent, "<", new Vector2(-200, y), new Vector2(90, 56), () => Page(-1));
        _pageLabel = MakeText(parent, "PageLabel", "PAGE  1 / 1", 22,
            new Color(1f, 1f, 1f, 0.6f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(280, 36));
        MakeButton(parent, ">", new Vector2(200, y), new Vector2(90, 56), () => Page(1));
    }

    void BuildJoinRow()
    {
        var label = MakeText(transform, "JoinLabel", "OR  TYPE  A  FRIEND'S  CODE", 18,
            new Color(1f, 1f, 1f, 0.4f), FontStyle.Normal,
            new Vector2(0.5f, 0.5f), new Vector2(0, -244), new Vector2(600, 26));
        _preRoomUi.Add(label.gameObject);

        // Code box, JOIN button beside it — one row, centered together.
        var box = MakeImage(transform, "JoinBox", new Color(0.04f, 0.10f, 0.16f, 0.95f));
        var boxRect = box.rectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = new Vector2(-100, -294);
        boxRect.sizeDelta = new Vector2(250, 62);

        var underline = MakeImage(box.transform, "Underline",
            new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.6f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(6, 0);
        underline.rectTransform.offsetMax = new Vector2(-6, 3);

        var text = MakeText(box.transform, "Text", "", 30, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 54));
        var placeholder = MakeText(box.transform, "Placeholder", "CODE", 30,
            new Color(1f, 1f, 1f, 0.2f), FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 54));

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

        var join = MakeButton(transform, "JOIN", new Vector2(130, -294), new Vector2(160, 62),
            () => NetSession.Ensure().Join(_joinField.text));

        _preRoomUi.Add(box.gameObject);
        _preRoomUi.Add(join);
    }

    GameObject MakeButton(Transform parent, string label, Vector2 position, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", ButtonColor);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

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

        MakeText(image.transform, "Label", label, Mathf.RoundToInt(size.y * 0.37f),
            Color.white, FontStyle.Bold, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(size.x - 20, size.y - 12));
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
