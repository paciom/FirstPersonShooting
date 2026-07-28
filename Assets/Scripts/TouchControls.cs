using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Roblox-style on-screen controls for touch screens: a dynamic thumbstick on
/// the left half, drag-anywhere-else to look, and big round action buttons on
/// the right (fire, jump, x-ray, morph, weapon cycle, menu).
///
/// Built entirely at runtime like the rest of the UI, and bootstrapped by
/// <see cref="GameModeController"/>, so no scene rebuild is needed. Reads raw
/// <c>Input.touches</c> rather than going through the EventSystem: fire + move
/// + look have to work as three simultaneous fingers, which uGUI buttons alone
/// won't give us.
///
/// They appear by themselves on a touch device; pressing '=' forces them on
/// (and off again) anywhere, with the mouse standing in for a finger.
///
/// Consumers (PlayerBrain, FlyCam) poll the held state directly and call the
/// Consume* methods for one-shot presses. Runs at execution order -100 so that
/// state is always fresh by the time those Updates read it.
/// </summary>
[DefaultExecutionOrder(-100)]
public class TouchControls : MonoBehaviour
{
    /// <summary>When the on-screen controls take over from mouse + keyboard.</summary>
    public enum Availability
    {
        /// <summary>Mobile always; desktop switches on the first real touch and back on the first WASD key. '=' overrides either way.</summary>
        Auto,
        Always,
        Never,
    }

    const float RefWidth = 1920f;
    const float RefHeight = 1080f;

    static readonly Color HoloCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color ButtonIdle = new Color(0.06f, 0.14f, 0.22f, 0.55f);
    static readonly Color ButtonHeld = new Color(0.2f, 0.9f, 1f, 0.8f);
    static readonly Color ButtonDim = new Color(0.06f, 0.10f, 0.14f, 0.35f);
    static readonly Color RingIdle = new Color(0.2f, 0.9f, 1f, 0.35f);

    // Role codes stored per finger. Buttons use their index in _buttons.
    const int RoleLook = -1;
    const int RoleStick = -2;

    public Availability availability = Availability.Auto;

    [Tooltip("Degrees turned per screen-height of drag, over 180. 0.55 ≈ a full-height swipe turns 99°.")]
    public float lookSensitivity = 0.55f;

    [Tooltip("Thumbstick travel in canvas units (reference height 1080).")]
    public float stickRadius = 135f;

    [Tooltip("Let the mouse stand in for a finger while the controls are forced on with '='.")]
    public bool simulateWithMouse = true;

    /// <summary>Keyboard toggle that forces the on-screen controls on and off.</summary>
    public const KeyCode ToggleKey = KeyCode.Equals;

    public static TouchControls Instance { get; private set; }

    /// <summary>True while the on-screen controls own player input.</summary>
    public static bool Active => Instance != null && Instance._active;

    /// <summary>Analog move, -1..1 per axis, same shape as the keyboard axes.</summary>
    public Vector2 Move { get; private set; }

    /// <summary>Look delta for this frame, in degrees — feed straight to CharacterMotor.AddLook.</summary>
    public Vector2 LookDelta { get; private set; }

    /// <summary>Pushing the stick to its rim sprints, so there's no separate sprint button.</summary>
    public bool Sprint => Move.sqrMagnitude > 0.85f;

    public bool Fire => Held(_fire);
    public bool Scope => Held(_scope);
    public bool Rise => Held(_rise);
    public bool Sink => Held(_sink);

    bool _active;
    bool _forced;                 // '=' override — show the controls on any device
    bool _sawTouch;
    bool _mouseWasSimulated = true;

    GameObject _root;
    RectTransform _canvasRect;
    Text _lookHint;
    bool _hasLooked;
    RectTransform _stickBase;
    RectTransform _stickKnob;
    Vector2 _stickHome;

    readonly List<Button> _buttons = new List<Button>();
    Button _fire, _jump, _scope, _snipe, _morph, _prevWeapon, _nextWeapon, _menu, _rise, _sink, _shuffle;

    readonly Dictionary<int, int> _roles = new Dictionary<int, int>();
    readonly Dictionary<int, Vector2> _positions = new Dictionary<int, Vector2>();
    readonly List<Pointer> _pointers = new List<Pointer>();
    readonly List<int> _stale = new List<int>();

    int _stickFinger = int.MinValue;
    Vector2 _stickCenter;

    bool _jumpPressed;
    int _weaponCycle;
    bool _morphPressed;
    bool _snipePressed;

    Vector3 _lastMousePosition;
    bool _mouseHeld;

    static Sprite _disc;
    static Sprite _ring;

    struct Pointer
    {
        public int id;
        public Vector2 position;
        public Vector2 delta;
        public TouchPhase phase;
    }

    /// <summary>Small round-button record — visuals plus its own pressed state.</summary>
    class Button
    {
        public RectTransform rect;
        public Image image;
        public Image rim;
        public Text label;
        public bool held;
        public bool dimmed;
        // activeInHierarchy, not activeSelf: a hidden canvas leaves its children
        // "active" locally, and a rect nobody can see must not eat taps.
        public bool Visible => rect != null && rect.gameObject.activeInHierarchy;
    }

    /// <summary>Create the controls if they don't exist yet. Safe to call repeatedly.</summary>
    public static TouchControls Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("TouchControls");
            go.AddComponent<TouchControls>();
        }
        return Instance;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Leave the mouse the way we found it — a stale false here would kill
        // menu clicking for the rest of the session.
        Input.simulateMouseWithTouches = true;
    }

    /// <summary>Jump edge, consumed by the reader so Update order can't drop it.</summary>
    public bool ConsumeJump()
    {
        bool pressed = _jumpPressed;
        _jumpPressed = false;
        return pressed;
    }

    /// <summary>-1 / +1 / 0 weapon-slot step from the arrow buttons.</summary>
    public int ConsumeWeaponCycle()
    {
        int cycle = _weaponCycle;
        _weaponCycle = 0;
        return cycle;
    }

    public bool ConsumeMorph()
    {
        bool pressed = _morphPressed;
        _morphPressed = false;
        return pressed;
    }

    /// <summary>Sniper scope toggle edge — see SniperScope for why it isn't a hold.</summary>
    public bool ConsumeSnipe()
    {
        bool pressed = _snipePressed;
        _snipePressed = false;
        return pressed;
    }

    void Update()
    {
        UpdateAvailability();

        if (!_active)
        {
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            ClearState();
            return;
        }

        if (_root == null)
            BuildUi();
        if (!_root.activeSelf)
            _root.SetActive(true);

        ApplyMode();
        if (!_root.activeSelf)
        {
            ClearState();
            return;
        }

        GatherPointers();
        ProcessPointers();
        UpdateVisuals();
    }

    // ---- availability -----------------------------------------------------

    void UpdateAvailability()
    {
        // '=' shows the on-screen controls on any device — handy for checking
        // the phone layout from a desktop, where the mouse then acts as a
        // single finger.
        if (Input.GetKeyDown(ToggleKey))
        {
            _forced = !_forced;
            Debug.Log($"TouchControls: on-screen controls {(_forced ? "forced ON" : "back to Auto")} ('=').");
        }

        if (Input.touchCount > 0)
            _sawTouch = true;

        // A real key on the movement cluster means someone picked the keyboard
        // back up (touch never synthesises those, unlike mouse buttons).
        if (_sawTouch && !Application.isMobilePlatform
            && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
                || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D)))
            _sawTouch = false;

        bool wanted = availability switch
        {
            Availability.Always => true,
            Availability.Never => false,
            _ => Application.isMobilePlatform || _sawTouch,
        };
        wanted |= _forced;

        if (wanted == _active)
            return;
        _active = wanted;

        // While the on-screen controls are up, touches must NOT masquerade as
        // mouse clicks — otherwise every look-drag also holds down "fire".
        Input.simulateMouseWithTouches = !_active;
        _mouseWasSimulated = !_active;

        if (_active)
        {
            // A captured cursor is meaningless here and hides the pointer we
            // use for editor testing.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        MainMenu.SetHint(_active
            ? "Left stick to move   ·   Drag to look   ·   FIRE / JUMP buttons"
            : null);
    }

    void ClearState()
    {
        Move = Vector2.zero;
        LookDelta = Vector2.zero;
        _roles.Clear();
        _positions.Clear();
        _stickFinger = int.MinValue;
        foreach (var button in _buttons)
            button.held = false;
        // Unread edges die with the mode — otherwise a jump tapped just before
        // the menu opened fires the moment the next match starts.
        _jumpPressed = false;
        _morphPressed = false;
        _snipePressed = false;
        _weaponCycle = 0;
        if (!_mouseWasSimulated)
        {
            Input.simulateMouseWithTouches = true;
            _mouseWasSimulated = true;
        }
    }

    /// <summary>Show only the widgets the current game mode can actually use.</summary>
    void ApplyMode()
    {
        GameMode mode = GameModeController.Instance != null
            ? GameModeController.Instance.Mode : GameMode.PlayerVsAI;

        bool playing = mode == GameMode.PlayerVsAI;
        bool flying = mode == GameMode.ArenaPreview;
        bool inMenu = mode == GameMode.Menu;

        // The menu draws its own buttons and covers the screen; anything of
        // ours underneath would just eat taps.
        _root.SetActive(!inMenu);
        if (inMenu)
            return;

        SetVisible(_stickBase, playing || flying);
        // Driven by its own alpha so the fade-out actually plays out.
        SetVisible(_lookHint != null ? _lookHint.rectTransform : null,
            (playing || flying) && _lookHint != null && _lookHint.color.a > 0.01f);
        SetVisible(_fire, playing);
        SetVisible(_jump, playing);
        SetVisible(_scope, playing);
        SetVisible(_snipe, playing);
        SetVisible(_prevWeapon, playing);
        SetVisible(_nextWeapon, playing);
        SetVisible(_rise, flying);
        SetVisible(_sink, flying);
        SetVisible(_shuffle, flying);
        SetVisible(_menu, true);

        if (!playing)
        {
            _jumpPressed = false;
            _morphPressed = false;
            _snipePressed = false;
            _weaponCycle = 0;
        }

        // MORPH stays on screen for the whole match so it can be found, but
        // dims on robots with no forged vehicle clips, where pressing it does
        // nothing. Hiding it instead made the control look like it came and
        // went with the robot.
        var vehicle = PlayerBrain.Local != null ? PlayerBrain.Local.GetComponent<TransformMode>() : null;
        SetVisible(_morph, playing && vehicle != null);
        SetDimmed(_morph, vehicle == null || !vehicle.CanTransform);
    }

    static void SetVisible(Button button, bool visible)
    {
        if (button != null)
            SetVisible(button.rect, visible);
    }

    /// <summary>Grey out a button whose action isn't available right now.</summary>
    static void SetDimmed(Button button, bool dimmed)
    {
        if (button != null)
            button.dimmed = dimmed;
    }

    static void SetVisible(RectTransform rect, bool visible)
    {
        if (rect != null && rect.gameObject.activeSelf != visible)
            rect.gameObject.SetActive(visible);
    }

    static bool Held(Button button) => button != null && button.held && button.Visible;

    // ---- input ------------------------------------------------------------

    void GatherPointers()
    {
        _pointers.Clear();
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            _pointers.Add(new Pointer
            {
                id = touch.fingerId,
                position = touch.position,
                delta = touch.deltaPosition,
                phase = touch.phase,
            });
        }

        // Stand-in finger so a forced-on layout is actually usable from a
        // desktop — without it, '=' would show buttons nothing can press.
        if (_pointers.Count == 0 && simulateWithMouse && (_forced || Application.isEditor))
        {
            Vector3 mouse = Input.mousePosition;
            bool down = Input.GetMouseButton(0);
            TouchPhase phase;
            if (down && !_mouseHeld) phase = TouchPhase.Began;
            else if (!down && _mouseHeld) phase = TouchPhase.Ended;
            else if (down) phase = TouchPhase.Moved;
            else phase = TouchPhase.Canceled;

            if (down || _mouseHeld)
            {
                _pointers.Add(new Pointer
                {
                    id = -100,
                    position = mouse,
                    // Zero on the press frame: the cursor travelled to the
                    // button-down point with nothing held, and charging that
                    // travel to the look drag snap-spins the camera. Real
                    // touches already report a zero delta on Began.
                    delta = phase == TouchPhase.Began ? Vector2.zero : (Vector2)(mouse - _lastMousePosition),
                    phase = phase,
                });
            }
            _mouseHeld = down;
            _lastMousePosition = mouse;
        }
    }

    void ProcessPointers()
    {
        _positions.Clear();
        foreach (var pointer in _pointers)
            _positions[pointer.id] = pointer.position;

        // Drop roles whose finger vanished (lifted, or lost to an app switch).
        _stale.Clear();
        foreach (var pair in _roles)
            if (!_positions.ContainsKey(pair.Key))
                _stale.Add(pair.Key);
        foreach (int id in _stale)
            _roles.Remove(id);
        if (_stickFinger != int.MinValue && !_roles.ContainsKey(_stickFinger))
            _stickFinger = int.MinValue;

        foreach (var pointer in _pointers)
        {
            if (pointer.phase == TouchPhase.Began)
                Assign(pointer);
            else if (pointer.phase == TouchPhase.Ended || pointer.phase == TouchPhase.Canceled)
                _roles.Remove(pointer.id);
        }

        // Held state is recomputed from the live roles every frame, so a finger
        // that disappears can never leave a button stuck down.
        foreach (var button in _buttons)
            button.held = false;

        Vector2 look = Vector2.zero;
        foreach (var pointer in _pointers)
        {
            if (!_roles.TryGetValue(pointer.id, out int role))
                continue;
            if (role == RoleLook)
                look += pointer.delta;
            else if (role >= 0 && role < _buttons.Count)
                _buttons[role].held = true;
        }

        // Screen-relative so the same swipe turns the same amount on a phone
        // and on a tablet.
        LookDelta = look / Mathf.Max(1, Screen.height) * 180f * lookSensitivity;

        // Turning is the one control with no widget to point at, so it gets a
        // label until the player has actually turned with it.
        if (Mathf.Abs(LookDelta.x) + Mathf.Abs(LookDelta.y) > 0.5f)
            _hasLooked = true;

        UpdateStick();
    }

    void Assign(Pointer pointer)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            if (!button.Visible)
                continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(button.rect, pointer.position, null))
                continue;
            _roles[pointer.id] = i;
            OnButtonDown(button);
            return;
        }

        // Roblox split: the lower-left corner is the stick, everything else
        // (including the top-left) turns the camera.
        bool stickZone = _stickBase != null && _stickBase.gameObject.activeInHierarchy
            && pointer.position.x < Screen.width * 0.45f
            && pointer.position.y < Screen.height * 0.7f;

        if (stickZone && _stickFinger == int.MinValue)
        {
            _roles[pointer.id] = RoleStick;
            _stickFinger = pointer.id;
            // Dynamic thumbstick: the ring jumps to wherever the thumb landed.
            _stickCenter = ToCanvas(pointer.position);
            _stickBase.anchoredPosition = _stickCenter;
        }
        else
        {
            _roles[pointer.id] = RoleLook;
        }
    }

    void OnButtonDown(Button button)
    {
        if (button == _jump) _jumpPressed = true;
        else if (button == _morph) _morphPressed = true;
        else if (button == _snipe) _snipePressed = true;
        else if (button == _prevWeapon) _weaponCycle = -1;
        else if (button == _nextWeapon) _weaponCycle = 1;
        else if (button == _menu && GameModeController.Instance != null)
            GameModeController.Instance.EnterMenu();
        else if (button == _shuffle && GameModeController.Instance != null)
            GameModeController.Instance.RequestReshuffle();
    }

    void UpdateStick()
    {
        bool stickShown = _stickBase != null && _stickBase.gameObject.activeInHierarchy;
        if (!stickShown || _stickFinger == int.MinValue
            || !_positions.TryGetValue(_stickFinger, out Vector2 screen))
        {
            Move = Vector2.zero;
            if (_stickBase != null)
                _stickBase.anchoredPosition = _stickHome;
            if (_stickKnob != null)
                _stickKnob.anchoredPosition = Vector2.zero;
            return;
        }

        Vector2 offset = ToCanvas(screen) - _stickCenter;
        Vector2 clamped = Vector2.ClampMagnitude(offset, stickRadius);
        Move = clamped / stickRadius;
        if (_stickKnob != null)
            _stickKnob.anchoredPosition = clamped;
    }

    /// <summary>Screen pixels → canvas-local units (canvas pivot is its centre).</summary>
    Vector2 ToCanvas(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
        return local;
    }

    void UpdateVisuals()
    {
        if (_lookHint != null)
        {
            Color color = _lookHint.color;
            color.a = Mathf.MoveTowards(color.a, _hasLooked ? 0f : 0.45f, 1.2f * Time.unscaledDeltaTime);
            _lookHint.color = color;
        }

        float blend = 18f * Time.unscaledDeltaTime;
        foreach (var button in _buttons)
        {
            if (button.image == null)
                continue;
            Color target = button.dimmed ? ButtonDim : (button.held ? ButtonHeld : ButtonIdle);
            button.image.color = Color.Lerp(button.image.color, target, blend);

            float ink = button.dimmed ? 0.3f : 1f;
            if (button.rim != null)
                button.rim.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.7f * ink);
            if (button.label != null)
                button.label.color = new Color(1f, 1f, 1f, ink);
        }
    }

    // ---- construction -----------------------------------------------------

    void BuildUi()
    {
        _root = new GameObject("TouchCanvas");
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD (0) and the mode overlay (10), below the menu (20).
        canvas.sortingOrder = 15;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _root.GetComponent<RectTransform>();

        BuildStick();
        BuildLookHint();

        // Right thumb: fire and jump where a Roblox player expects them, with
        // the situational buttons stacked above and inboard.
        _fire = MakeRoundButton("Fire", "FIRE", new Vector2(1, 0), new Vector2(-370, 280), 210);
        _jump = MakeRoundButton("Jump", "JUMP", new Vector2(1, 0), new Vector2(-150, 150), 170);
        _scope = MakeRoundButton("Scope", "X-RAY", new Vector2(1, 0), new Vector2(-175, 470), 140);
        _snipe = MakeRoundButton("Snipe", "SNIPE", new Vector2(1, 0), new Vector2(-175, 630), 140);
        _morph = MakeRoundButton("Morph", "MORPH", new Vector2(1, 0), new Vector2(-420, 545), 140);
        _prevWeapon = MakeRoundButton("PrevWeapon", "<", new Vector2(1, 0), new Vector2(-700, 150), 110);
        _nextWeapon = MakeRoundButton("NextWeapon", ">", new Vector2(1, 0), new Vector2(-570, 150), 110);

        // Arena Builder fly-cam extras.
        _rise = MakeRoundButton("Rise", "UP", new Vector2(1, 0), new Vector2(-170, 330), 150);
        _sink = MakeRoundButton("Sink", "DOWN", new Vector2(1, 0), new Vector2(-370, 180), 150);
        _shuffle = MakeRoundButton("Shuffle", "NEW", new Vector2(1, 0), new Vector2(-170, 520), 150);

        // No Escape key on a tablet — this is the only way back to the menu.
        // Top-LEFT, where Roblox puts it, which also leaves the opposite corner
        // free for the transformation replay (TransformCast).
        _menu = MakeRoundButton("Menu", "MENU", new Vector2(0, 1), new Vector2(130, -110), 150);
    }

    void BuildStick()
    {
        _stickHome = new Vector2(-RefWidth * 0.5f + 300f, -RefHeight * 0.5f + 300f);

        var baseGo = new GameObject("StickBase");
        baseGo.transform.SetParent(_root.transform, false);
        var baseImage = baseGo.AddComponent<Image>();
        baseImage.sprite = RingSprite();
        baseImage.color = RingIdle;
        baseImage.raycastTarget = false;
        _stickBase = baseImage.rectTransform;
        _stickBase.anchorMin = _stickBase.anchorMax = new Vector2(0.5f, 0.5f);
        _stickBase.sizeDelta = Vector2.one * (stickRadius * 2f);
        _stickBase.anchoredPosition = _stickHome;

        var knobGo = new GameObject("StickKnob");
        knobGo.transform.SetParent(_stickBase, false);
        var knobImage = knobGo.AddComponent<Image>();
        knobImage.sprite = DiscSprite();
        knobImage.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.65f);
        knobImage.raycastTarget = false;
        _stickKnob = knobImage.rectTransform;
        _stickKnob.anchorMin = _stickKnob.anchorMax = new Vector2(0.5f, 0.5f);
        _stickKnob.sizeDelta = Vector2.one * (stickRadius * 0.95f);
        _stickKnob.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Sits over the free part of the look area (right of the stick, clear of
    /// the action buttons) and fades out for good once the player turns.
    /// </summary>
    void BuildLookHint()
    {
        var go = new GameObject("LookHint");
        go.transform.SetParent(_root.transform, false);
        _lookHint = go.AddComponent<Text>();
        _lookHint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _lookHint.text = "DRAG HERE TO TURN";
        _lookHint.fontSize = 34;
        _lookHint.fontStyle = FontStyle.Bold;
        _lookHint.alignment = TextAnchor.MiddleCenter;
        _lookHint.color = new Color(1f, 1f, 1f, 0.45f);
        _lookHint.raycastTarget = false;
        var rect = _lookHint.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(RefWidth * 0.2f, RefHeight * 0.12f);
        rect.sizeDelta = new Vector2(700, 60);
    }

    Button MakeRoundButton(string name, string label, Vector2 anchor, Vector2 position, float size)
    {
        var go = new GameObject($"Touch_{name}");
        go.transform.SetParent(_root.transform, false);
        var image = go.AddComponent<Image>();
        image.sprite = DiscSprite();
        image.color = ButtonIdle;
        // We hit-test these ourselves; leaving them raycastable would let them
        // steal taps from the menus stacked above.
        image.raycastTarget = false;

        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = Vector2.one * size;

        var rim = new GameObject("Rim");
        rim.transform.SetParent(rect, false);
        var rimImage = rim.AddComponent<Image>();
        rimImage.sprite = RingSprite();
        rimImage.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.7f);
        rimImage.raycastTarget = false;
        var rimRect = rimImage.rectTransform;
        rimRect.anchorMin = Vector2.zero;
        rimRect.anchorMax = Vector2.one;
        rimRect.offsetMin = Vector2.zero;
        rimRect.offsetMax = Vector2.zero;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(rect, false);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = Mathf.RoundToInt(size * 0.22f);
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var button = new Button { rect = rect, image = image, rim = rimImage, label = text };
        _buttons.Add(button);
        return button;
    }

    static Sprite DiscSprite()
    {
        if (_disc == null) _disc = BuildCircle(0f);
        return _disc;
    }

    static Sprite RingSprite()
    {
        if (_ring == null) _ring = BuildCircle(0.86f);
        return _ring;
    }

    /// <summary>
    /// White circle (or ring, when innerFraction > 0) with a one-pixel feathered
    /// edge — the project ships no sprite atlas, so the UI grows its own.
    /// </summary>
    static Sprite BuildCircle(float innerFraction)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = innerFraction > 0f ? "TouchRing" : "TouchDisc",
        };
        texture.hideFlags = HideFlags.HideAndDontSave;

        float outer = size * 0.5f - 1f;
        float inner = outer * innerFraction;
        var pixels = new Color32[size * size];
        var centre = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                float alpha = Mathf.Clamp01(outer - distance);
                if (inner > 0f)
                    alpha = Mathf.Min(alpha, Mathf.Clamp01(distance - inner));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
