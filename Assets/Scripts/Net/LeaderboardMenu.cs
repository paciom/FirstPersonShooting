using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The LEADERBOARDS screen: a mode down the left, an arena across the top,
/// the top ten pilots in the middle and where YOU stand underneath. Runtime
/// uGUI in the AccountMenu style; opens over the main menu and hands it
/// back on BACK / Escape.
///
/// Boards come from <see cref="LeaderboardClient.Fetch"/>. Offline, or as a
/// guest, the screen still shows this device's personal best so a kid with
/// no account is not staring at an empty page.
/// </summary>
public static class LeaderboardMenu
{
    /// <summary>The mode the screen opens on; the main menu leaves it at
    /// whatever the player last viewed, a results screen sets it.</summary>
    public static string OpenOnMode = "gunfight";
    public static string OpenOnArena = LeaderboardClient.AllArenas;

    public static void Open(GameModeController controller, GameObject mainMenuCanvas)
    {
        mainMenuCanvas.SetActive(false);

        var canvasGo = new GameObject("LeaderboardMenu");
        canvasGo.transform.SetParent(controller.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var ui = canvasGo.AddComponent<LeaderboardMenuUi>();
        ui.Init(controller, mainMenuCanvas);
    }
}

public class LeaderboardMenuUi : MonoBehaviour
{
    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    static readonly Color Silver = new Color(0.85f, 0.88f, 0.95f);
    static readonly Color Bronze = new Color(0.9f, 0.6f, 0.35f);
    static readonly Color ButtonColor = new Color(0.06f, 0.14f, 0.22f, 0.95f);
    static readonly Color ButtonHover = new Color(0.10f, 0.30f, 0.42f, 1f);
    static readonly Color TabActive = new Color(0.10f, 0.30f, 0.42f, 1f);
    static readonly Color RowColor = new Color(0.04f, 0.10f, 0.16f, 0.85f);
    static readonly Color RowAlt = new Color(0.05f, 0.12f, 0.19f, 0.85f);
    static readonly Color MeColor = new Color(0.10f, 0.32f, 0.22f, 0.95f);
    static readonly Color OkColor = new Color(0.4f, 1f, 0.6f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);

    const int Rows = 10;

    // Layout on the 1920x1080 reference canvas, centre-anchored.
    const float ModeColumnX = -720f;
    const float ModeTop = 250f, ModePitch = 58f;
    const float BoardX = 130f, BoardW = 1040f;
    const float ArenaRowY = 268f, ArenaRow2Y = 220f;
    const float RowTop = 150f, RowPitch = 40f;

    GameModeController _controller;
    GameObject _mainMenuCanvas;
    string _mode;
    string _arena;
    int _requestSerial;

    readonly Dictionary<string, GameObject> _modeTabs = new Dictionary<string, GameObject>();
    readonly List<GameObject> _arenaTabs = new List<GameObject>();
    RectTransform _arenaRoot;
    Text _boardTitle, _unitHeader, _status, _mine;
    Button _signInButton;
    readonly Text[] _rankText = new Text[Rows];
    readonly Text[] _nameText = new Text[Rows];
    readonly Text[] _scoreText = new Text[Rows];
    readonly Image[] _rowImage = new Image[Rows];

    public void Init(GameModeController controller, GameObject mainMenuCanvas)
    {
        _controller = controller;
        _mainMenuCanvas = mainMenuCanvas;
        _mode = LeaderboardMenu.OpenOnMode;
        _arena = LeaderboardMenu.OpenOnArena;
        if (System.Array.IndexOf(LeaderboardClient.RankedModes, _mode) < 0)
            _mode = LeaderboardClient.RankedModes[0];
        Build();
        AccountClient.Changed += OnAccountChanged;
        SelectMode(_mode, _arena);
    }

    void OnDestroy()
    {
        AccountClient.Changed -= OnAccountChanged;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Back();
    }

    void Back()
    {
        if (_mainMenuCanvas != null)
            _mainMenuCanvas.SetActive(true);
        Destroy(gameObject);
    }

    void OnAccountChanged()
    {
        Load();
    }

    // ----------------------------------------------------------- selection

    void SelectMode(string mode, string arena)
    {
        _mode = mode;
        LeaderboardMenu.OpenOnMode = mode;
        foreach (var pair in _modeTabs)
            SetTabColors(pair.Value, pair.Key == mode);
        BuildArenaTabs();
        var arenas = LeaderboardClient.ArenasFor(mode);
        if (arena != LeaderboardClient.AllArenas && System.Array.IndexOf(arenas, arena) < 0)
            arena = LeaderboardClient.AllArenas;
        SelectArena(arena);
    }

    void SelectArena(string arena)
    {
        _arena = arena;
        LeaderboardMenu.OpenOnArena = arena;
        for (int i = 0; i < _arenaTabs.Count; i++)
        {
            string tabArena = _arenaTabs[i].name.Substring("Arena_".Length);
            SetTabColors(_arenaTabs[i], tabArena == arena);
        }
        _boardTitle.text = LeaderboardClient.ModeTitle(_mode) + "   ·   " +
            (arena == LeaderboardClient.AllArenas ? "ALL  ARENAS" : arena.ToUpperInvariant());
        _unitHeader.text = LeaderboardClient.ModeUnit(_mode).ToUpperInvariant();
        Load();
    }

    /// <summary>Fetch the current board; stale replies are dropped by serial.</summary>
    void Load()
    {
        int serial = ++_requestSerial;
        ClearRows();
        ShowMine();
        var account = AccountClient.Instance;
        bool signedIn = account != null && account.SignedIn;
        _signInButton.gameObject.SetActive(!signedIn);

        if (AccountClient.ApiBase == null)
        {
            _status.text = "no leaderboard server is set up";
            _status.color = Dim;
            return;
        }
        _status.text = "loading...";
        _status.color = Dim;
        LeaderboardClient.Fetch(_mode, _arena, Rows, reply =>
        {
            if (this == null || serial != _requestSerial)
                return;
            if (reply == null)
            {
                _status.text = "can't reach the leaderboard right now";
                _status.color = new Color(1f, 0.6f, 0.5f);
                return;
            }
            if (!reply.ok)
            {
                _status.text = string.IsNullOrEmpty(reply.message) ? "something went wrong" : reply.message;
                _status.color = new Color(1f, 0.6f, 0.5f);
                return;
            }
            FillRows(reply);
            ShowMine(reply);
        });
    }

    void ClearRows()
    {
        for (int i = 0; i < Rows; i++)
        {
            _rankText[i].text = (i + 1).ToString();
            _nameText[i].text = "";
            _scoreText[i].text = "";
            _rowImage[i].color = i % 2 == 0 ? RowColor : RowAlt;
            _nameText[i].color = Dim;
            _scoreText[i].color = Dim;
        }
    }

    void FillRows(LeaderboardClient.BoardReply reply)
    {
        int count = reply.rows != null ? reply.rows.Length : 0;
        if (count == 0)
        {
            _status.text = "nobody has set a score here yet  -  be the first!";
            _status.color = Dim;
            return;
        }
        _status.text = "";
        for (int i = 0; i < Rows && i < count; i++)
        {
            var row = reply.rows[i];
            _nameText[i].text = row.username.ToUpperInvariant();
            _scoreText[i].text = Format(row.score);
            Color tint = row.rank == 1 ? Gold : row.rank == 2 ? Silver : row.rank == 3 ? Bronze : Color.white;
            _nameText[i].color = tint;
            _scoreText[i].color = tint;
            if (row.me)
                _rowImage[i].color = MeColor;
        }
    }

    /// <summary>The YOU line: server rank when known, else this device's best.</summary>
    void ShowMine(LeaderboardClient.BoardReply reply = null)
    {
        var account = AccountClient.Instance;
        bool signedIn = account != null && account.SignedIn;
        int local = LeaderboardClient.LocalBest(_mode, _arena);
        string unit = LeaderboardClient.ModeUnit(_mode);

        if (reply != null && signedIn && reply.myScore >= 0)
        {
            _mine.text = reply.myRank > 0
                ? $"YOU   ·   #{reply.myRank}   ·   {Format(reply.myScore)}  {unit}"
                : $"YOU   ·   {Format(reply.myScore)}  {unit}   ·   keep climbing!";
            _mine.color = OkColor;
            return;
        }
        if (local >= 0)
        {
            _mine.text = signedIn
                ? $"YOUR  BEST   ·   {Format(local)}  {unit}"
                : $"YOUR  BEST  ON  THIS  DEVICE   ·   {Format(local)}  {unit}   ·   sign in to get on the board";
            _mine.color = signedIn ? OkColor : Dim;
            return;
        }
        _mine.text = signedIn
            ? "play a match to set your first score"
            : "sign in and your scores join the board";
        _mine.color = Dim;
    }

    static string Format(int score) => score.ToString("N0");

    static void SetTabColors(GameObject tab, bool active)
    {
        tab.GetComponent<Image>().color = active ? TabActive : ButtonColor;
        tab.transform.Find("Label").GetComponent<Text>().color =
            active ? HoloCyan : new Color(1f, 1f, 1f, 0.6f);
    }

    // ------------------------------------------------------------ building

    void Build()
    {
        var backdrop = MakeImage(transform, "Backdrop", new Color(0.01f, 0.03f, 0.06f, 0.9f));
        Stretch(backdrop.rectTransform);

        MakeText(transform, "Title", "LEADERBOARDS", 64, HoloCyan, FontStyle.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(900, 90));
        MakeText(transform, "Subtitle", "TOP  PILOTS   ·   EVERY  BATTLE   ·   EVERY  ARENA", 22,
            new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(1100, 34));

        // Mode column.
        var modeHeading = MakeText(transform, "ModeHeading", "GAME", 18, Dim, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(ModeColumnX, ModeTop + 46f), new Vector2(300, 24));
        modeHeading.alignment = TextAnchor.MiddleLeft;
        for (int i = 0; i < LeaderboardClient.RankedModes.Length; i++)
        {
            string mode = LeaderboardClient.RankedModes[i];
            var tab = MakeTab(transform, "Mode_" + mode, LeaderboardClient.ModeTitle(mode),
                new Vector2(ModeColumnX, ModeTop - i * ModePitch), new Vector2(300, 50), 20,
                () => SelectMode(mode, LeaderboardClient.AllArenas));
            _modeTabs[mode] = tab;
        }

        // Arena chips live under their own root so a mode switch can wipe them.
        _arenaRoot = MakeRect(transform, "Arenas");
        Stretch(_arenaRoot);

        _boardTitle = MakeText(transform, "BoardTitle", "", 30, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), new Vector2(BoardX, RowTop + 62f), new Vector2(BoardW, 40));

        // Column headers.
        var header = MakeImage(transform, "Header", new Color(0.08f, 0.2f, 0.3f, 0.9f));
        Place(header.rectTransform, new Vector2(BoardX, RowTop + 22f), new Vector2(BoardW, 30));
        var rankHead = MakeText(header.transform, "RankHead", "#", 16, Dim, FontStyle.Bold,
            new Vector2(0f, 0.5f), new Vector2(50, 0), new Vector2(60, 28));
        rankHead.alignment = TextAnchor.MiddleLeft;
        var nameHead = MakeText(header.transform, "NameHead", "PILOT", 16, Dim, FontStyle.Bold,
            new Vector2(0f, 0.5f), new Vector2(120, 0), new Vector2(600, 28));
        nameHead.alignment = TextAnchor.MiddleLeft;
        _unitHeader = MakeText(header.transform, "UnitHead", "", 16, Dim, FontStyle.Bold,
            new Vector2(1f, 0.5f), new Vector2(-130, 0), new Vector2(220, 28));
        _unitHeader.alignment = TextAnchor.MiddleRight;

        for (int i = 0; i < Rows; i++)
        {
            var row = MakeImage(transform, "Row" + i, i % 2 == 0 ? RowColor : RowAlt);
            Place(row.rectTransform, new Vector2(BoardX, RowTop - i * RowPitch), new Vector2(BoardW, RowPitch - 4f));
            _rowImage[i] = row;
            _rankText[i] = MakeText(row.transform, "Rank", (i + 1).ToString(), 22, Dim, FontStyle.Bold,
                new Vector2(0f, 0.5f), new Vector2(50, 0), new Vector2(60, 34));
            _rankText[i].alignment = TextAnchor.MiddleLeft;
            _nameText[i] = MakeText(row.transform, "Name", "", 22, Color.white, FontStyle.Bold,
                new Vector2(0f, 0.5f), new Vector2(120, 0), new Vector2(600, 34));
            _nameText[i].alignment = TextAnchor.MiddleLeft;
            _scoreText[i] = MakeText(row.transform, "Score", "", 22, Color.white, FontStyle.Bold,
                new Vector2(1f, 0.5f), new Vector2(-130, 0), new Vector2(220, 34));
            _scoreText[i].alignment = TextAnchor.MiddleRight;
        }

        float footY = RowTop - Rows * RowPitch - 8f;
        _status = MakeText(transform, "Status", "", 20, Dim, FontStyle.Italic,
            new Vector2(0.5f, 0.5f), new Vector2(BoardX, RowTop - 4.5f * RowPitch), new Vector2(BoardW, 34));

        var mineBox = MakeImage(transform, "Mine", new Color(0.03f, 0.08f, 0.13f, 0.95f));
        Place(mineBox.rectTransform, new Vector2(BoardX, footY - 14f), new Vector2(BoardW, 44));
        var underline = MakeImage(mineBox.transform, "Underline", new Color(OkColor.r, OkColor.g, OkColor.b, 0.5f));
        underline.rectTransform.anchorMin = new Vector2(0, 0);
        underline.rectTransform.anchorMax = new Vector2(1, 0);
        underline.rectTransform.offsetMin = new Vector2(6, 0);
        underline.rectTransform.offsetMax = new Vector2(-6, 2);
        _mine = MakeText(mineBox.transform, "Label", "", 20, OkColor, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BoardW - 20f, 40));

        var signIn = MakeButton(transform, "SIGN  IN", new Vector2(ModeColumnX, -340f), new Vector2(300, 60), 24,
            () =>
            {
                if (_mainMenuCanvas != null)
                    AccountMenu.Open(_controller, _mainMenuCanvas);
                Destroy(gameObject);
            });
        _signInButton = signIn.GetComponent<Button>();

        MakeButton(transform, "BACK", new Vector2(ModeColumnX, -420f), new Vector2(300, 60), 24, Back);
    }

    void BuildArenaTabs()
    {
        foreach (var tab in _arenaTabs)
            Destroy(tab);
        _arenaTabs.Clear();

        var names = new List<string> { LeaderboardClient.AllArenas };
        names.AddRange(LeaderboardClient.ArenasFor(_mode));

        // Two rows of chips across the board's width; the chip width comes
        // down as the list grows so every arena stays reachable without
        // scrolling (the biggest roster is Gunfight's arena library).
        int perRow = Mathf.Max(1, Mathf.CeilToInt(names.Count / 2f));
        if (names.Count <= 6) perRow = names.Count;
        float pitch = BoardW / perRow;
        float chipW = Mathf.Min(220f, pitch - 8f);
        int fontSize = chipW < 120f ? 13 : chipW < 170f ? 15 : 17;
        for (int i = 0; i < names.Count; i++)
        {
            string arena = names[i];
            int row = i / perRow, col = i % perRow;
            int inRow = row == 0 ? Mathf.Min(perRow, names.Count) : names.Count - perRow;
            float rowWidth = inRow * pitch;
            float x = BoardX - rowWidth * 0.5f + pitch * (col + 0.5f);
            float y = row == 0 ? ArenaRowY : ArenaRow2Y;
            string label = arena == LeaderboardClient.AllArenas ? "ALL  ARENAS" : arena.ToUpperInvariant();
            var tab = MakeTab(_arenaRoot, "Arena_" + arena, label, new Vector2(x, y),
                new Vector2(chipW, 40), fontSize, () => SelectArena(arena));
            _arenaTabs.Add(tab);
        }
    }

    // ------------------------------------------------- widget construction

    GameObject MakeTab(Transform parent, string name, string label, Vector2 at, Vector2 size,
        int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, name, ButtonColor);
        image.sprite = MainMenu.RoundedTile();
        image.type = Image.Type.Sliced;
        Place(image.rectTransform, at, size);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        var text = MakeText(image.transform, "Label", label, fontSize, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 12f, size.y - 6f));
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = fontSize;
        return image.gameObject;
    }

    GameObject MakeButton(Transform parent, string label, Vector2 at, Vector2 size, int fontSize,
        UnityEngine.Events.UnityAction onClick)
    {
        var image = MakeImage(parent, $"Button_{label}", ButtonColor);
        Place(image.rectTransform, at, size);
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

        MakeText(image.transform, "Label", label, fontSize, Color.white, FontStyle.Bold,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 20f, size.y - 10f));
        return image.gameObject;
    }

    static RectTransform MakeRect(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
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
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
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
