using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Roblox-style on-screen controls for touch screens: a dynamic thumbstick on
/// the left half to walk, a fixed aim stick on the lower right to turn the
/// body and gun, and big round action buttons (fire, jump, x-ray, snipe,
/// morph, weapon cycle, menu). Drag anywhere that isn't a widget still looks.
///
/// True twin-stick: the aim stick steers at a speed set by how far it is
/// pushed, and FIRE is a plain button — one above each stick, so either thumb
/// can shoot while the other keeps walking or aiming.
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
    const int RoleAim = -3;

    public Availability availability = Availability.Auto;

    [Tooltip("Degrees turned per screen-height of drag, over 180. 1.0 ≈ a full-height swipe turns 180°.")]
    public float lookSensitivity = 1.0f;

    [Tooltip("Thumbstick travel in canvas units (reference height 1080).")]
    public float stickRadius = 135f;

    [Tooltip("Travel on the AIM stick before it is steering at full rate.")]
    public float aimStickRadius = 160f;

    [Tooltip("Degrees per second the body turns at full deflection of the AIM stick.")]
    public float turnDegreesPerSecond = 170f;

    [Tooltip("Degrees per second the aim rises or drops at full deflection.")]
    public float pitchDegreesPerSecond = 110f;

    [Tooltip("Fraction of the AIM stick's travel that doesn't steer, so a resting thumb doesn't drift the camera.")]
    [Range(0f, 0.5f)] public float aimStickDeadZone = 0.10f;

    /// <summary>
    /// Shape of the AIM stick's response: rate = full × deflection^expo.
    ///
    /// This is the difference between a stick you can steer with and a stick you
    /// can AIM with. A linear stick spends its useful range in the first
    /// centimetre of thumb travel — a nudge meant to nudge the crosshair swings
    /// it past the target, and the only way to correct is another overshoot the
    /// other way. Squaring it makes the bottom of the range fine and the top
    /// unchanged: a third of the way out turns at ~11°/s where it used to turn
    /// at ~32°/s, while the rim still whips round at the full rate — so the
    /// precision is bought from the middle of the range, not from the top end.
    ///
    /// 1 restores the old linear feel; higher is finer near the centre.
    /// </summary>
    [Tooltip("Response curve of the AIM stick. 1 is linear; 2 gives fine control near the centre " +
             "and the same top speed at the rim.")]
    [Range(1f, 3f)] public float aimExpo = 2f;

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

    /// <summary>
    /// Steering from the aim stick, -1..1 per axis. Already folded into
    /// <see cref="LookDelta"/> — exposed for readouts, not for driving the
    /// motor a second time.
    /// </summary>
    public Vector2 Turn { get; private set; }

    /// <summary>Twin FIRE buttons, one above each stick — either one shoots.</summary>
    public bool Fire => Held(_fireLeft) || Held(_fireRight);
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
    RectTransform _aimBase;
    RectTransform _aimKnob;
    int _aimFinger = int.MinValue;
    Vector2 _aimCenter;

    readonly List<Button> _buttons = new List<Button>();
    Button _fireLeft, _fireRight, _jump, _scope, _snipe, _morph, _prevWeapon, _nextWeapon, _menu, _rise, _sink, _shuffle;

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

    /// <summary>
    /// Whether a screen point sits on one of our visible buttons. The buttons
    /// are hand-hit-tested, not uGUI Buttons, so the EventSystem's
    /// IsPointerOverGameObject can't see them — anything that treats clicks
    /// as world input (Commander's selection) must ask here too, or a tap on
    /// MENU also lands in the world underneath it.
    /// </summary>
    public static bool PointOver(Vector2 screenPoint)
    {
        if (Instance == null || !Active)
            return false;
        foreach (var button in Instance._buttons)
            if (button.Visible &&
                RectTransformUtility.RectangleContainsScreenPoint(button.rect, screenPoint, null))
                return true;
        return false;
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
            ? "Left stick to move   ·   Right stick to aim   ·   FIRE with either thumb"
            : null);
    }

    void ClearState()
    {
        Move = Vector2.zero;
        LookDelta = Vector2.zero;
        Turn = Vector2.zero;
        _roles.Clear();
        _positions.Clear();
        _stickFinger = int.MinValue;
        _aimFinger = int.MinValue;
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

        var vehicle = PlayerBrain.Local != null ? PlayerBrain.Local.GetComponent<TransformMode>() : null;
        // A tank has no legs to leap with — CharacterMotor.Jump() already
        // refuses, so an on-screen JUMP would be a button that does nothing.
        // Mid-fold counts as tank too, matching that rule.
        bool canJump = vehicle == null || !(vehicle.IsVehicle || vehicle.IsBusy);

        // The menu draws its own buttons and covers the screen; anything of
        // ours underneath would just eat taps.
        _root.SetActive(!inMenu);
        if (inMenu)
            return;

        SetVisible(_stickBase, playing || flying);
        // The aim stick only exists where there is a body to steer — the
        // fly-cam keeps drag-to-look, and its UP/DOWN buttons sit where the
        // stick would go.
        SetVisible(_aimBase, playing);
        // Driven by its own alpha so the fade-out actually plays out.
        SetVisible(_lookHint != null ? _lookHint.rectTransform : null,
            (playing || flying) && _lookHint != null && _lookHint.color.a > 0.01f);
        SetVisible(_fireLeft, playing);
        SetVisible(_fireRight, playing);
        SetVisible(_jump, playing && canJump);
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
        // A jump tapped in the instant before the fold started has nowhere to
        // land now; letting it sit would pop the robot the moment it unfolds.
        if (!canJump)
            _jumpPressed = false;

        // MORPH stays on screen for the whole match so it can be found, but
        // dims on robots with no forged vehicle clips, where pressing it does
        // nothing. Hiding it instead made the control look like it came and
        // went with the robot.
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
        if (_aimFinger != int.MinValue && !_roles.ContainsKey(_aimFinger))
            _aimFinger = int.MinValue;

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

        // The aim stick steers at a RATE rather than by displacement: held off
        // centre it keeps turning, and the further out, the faster. Folded into
        // the same look delta so it inherits the sniper scope's fine aim.
        UpdateAimStick();
        LookDelta += new Vector2(Turn.x * turnDegreesPerSecond, Turn.y * pitchDegreesPerSecond)
            * Time.deltaTime;

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

        // The aim stick is a fixed target the thumb returns to blind, so a
        // grab a little outside the ring still counts — mid-fight thumbs are
        // not precise.
        if (_aimBase != null && _aimBase.gameObject.activeInHierarchy && _aimFinger == int.MinValue)
        {
            Vector2 center = ToCanvas(RectTransformUtility.WorldToScreenPoint(null, _aimBase.position));
            // Against the AIM stick's radius, not the move stick's — they are
            // different sizes now, and a grab zone smaller than the ring means
            // a thumb landing on the rim falls through to drag-to-look instead.
            // Only the free look area can lose a touch here: the buttons are
            // tested first and return before this.
            if ((ToCanvas(pointer.position) - center).magnitude <= aimStickRadius * 1.25f)
            {
                _roles[pointer.id] = RoleAim;
                _aimFinger = pointer.id;
                // Steering is measured from the ring's centre, not from where
                // the thumb landed: grabbing the rim starts turning at once,
                // like a gamepad stick held over.
                _aimCenter = center;
                return;
            }
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

    /// <summary>
    /// The right-hand stick: push to swing the body and gun that way. Fixed
    /// centre, so deflection is measured from the ring no matter where the
    /// grab landed.
    /// </summary>
    void UpdateAimStick()
    {
        if (_aimFinger == int.MinValue || !_positions.TryGetValue(_aimFinger, out Vector2 screen)
            || _aimBase == null || !_aimBase.gameObject.activeInHierarchy)
        {
            Turn = Vector2.zero;
            if (_aimKnob != null)
                _aimKnob.anchoredPosition = Vector2.zero;
            return;
        }

        Vector2 offset = Vector2.ClampMagnitude(ToCanvas(screen) - _aimCenter, aimStickRadius);
        if (_aimKnob != null)
            _aimKnob.anchoredPosition = offset;

        // Rescaled past the dead zone rather than clipped, so the first degree
        // of steering is gentle instead of arriving at full speed, then curved
        // by aimExpo so the bottom of the range is fine enough to aim with.
        Vector2 raw = offset / aimStickRadius;
        float magnitude = raw.magnitude;
        if (magnitude <= aimStickDeadZone)
        {
            Turn = Vector2.zero;
            return;
        }

        float travel = (magnitude - aimStickDeadZone) / (1f - aimStickDeadZone);
        Turn = raw.normalized * Mathf.Pow(travel, aimExpo);
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
        BuildAimStick();
        BuildLookHint();

        // Right thumb: the aim stick rests where FIRE used to sit, with FIRE
        // now a plain button just above it. A mirrored FIRE above the move
        // stick lets the left thumb shoot while the right stays on the aim.
        _fireRight = MakeRoundButton("FireRight", "FIRE", new Vector2(1, 0), new Vector2(-370, 545), 180);
        _fireLeft = MakeRoundButton("FireLeft", "FIRE", new Vector2(0, 0), new Vector2(300, 620), 180);
        _jump = MakeRoundButton("Jump", "JUMP", new Vector2(1, 0), new Vector2(-150, 150), 170);
        _scope = MakeRoundButton("Scope", "X-RAY", new Vector2(1, 0), new Vector2(-175, 470), 140);
        _snipe = MakeRoundButton("Snipe", "SNIPE", new Vector2(1, 0), new Vector2(-175, 630), 140);
        _morph = MakeRoundButton("Morph", "MORPH", new Vector2(1, 0), new Vector2(-620, 480), 140);
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

        _stickKnob = MakeStickKnob(_stickBase, stickRadius * 0.95f);
    }

    /// <summary>
    /// The right-hand aim stick. Fixed rather than dynamic: it is a target the
    /// thumb returns to blind mid-fight, so it must always be in the same
    /// place — drag-to-look covers casual turning everywhere else.
    /// </summary>
    void BuildAimStick()
    {
        var baseGo = new GameObject("AimBase");
        baseGo.transform.SetParent(_root.transform, false);
        var baseImage = baseGo.AddComponent<Image>();
        baseImage.sprite = RingSprite();
        baseImage.color = RingIdle;
        baseImage.raycastTarget = false;
        _aimBase = baseImage.rectTransform;
        _aimBase.anchorMin = _aimBase.anchorMax = new Vector2(1f, 0f);
        // Sized from its OWN radius, not the move stick's: the ring is the map
        // of where the thumb can go, and a knob that travels outside it reads as
        // a control that has come apart. Wider than the move stick on purpose —
        // this is the aiming control, and travel is what fine aim is made of.
        _aimBase.sizeDelta = Vector2.one * (aimStickRadius * 2f);
        _aimBase.anchoredPosition = new Vector2(-370f, 280f);

        _aimKnob = MakeStickKnob(_aimBase, aimStickRadius * 0.8f);
    }

    /// <summary>The bit that follows the thumb. Drawn last so it rides over its base.</summary>
    RectTransform MakeStickKnob(RectTransform parent, float size)
    {
        var go = new GameObject("Knob");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = DiscSprite();
        image.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.65f);
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one * size;
        rect.anchoredPosition = Vector2.zero;
        return rect;
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
