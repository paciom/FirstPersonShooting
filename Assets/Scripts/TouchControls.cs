using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Roblox-style on-screen controls for touch screens: a dynamic thumbstick on
/// the left half to walk, a fixed aim stick on the lower right to turn the
/// body and gun, and big round action buttons (fire, jump, x-ray, snipe, arms,
/// weapon cycle, menu). Drag anywhere that isn't a widget still looks.
///
/// Morphing has no button here: the form dial in the top-right corner is both
/// the readout and the control. It belongs to TransformCast, which draws it —
/// <see cref="SetFormDial"/> is how its taps come back to this input path.
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
    /// <summary>Internal alongside the disc and ring sprites — see DiscSprite.</summary>
    internal static readonly Color ButtonIdle = new Color(0.06f, 0.14f, 0.22f, 0.55f);
    static readonly Color ButtonHeld = new Color(0.2f, 0.9f, 1f, 0.8f);
    static readonly Color ButtonDim = new Color(0.06f, 0.10f, 0.14f, 0.35f);
    static readonly Color RingIdle = new Color(0.2f, 0.9f, 1f, 0.35f);

    // Role codes stored per finger. Buttons use their index in _buttons.
    const int RoleLook = -1;
    const int RoleStick = -2;
    const int RoleAim = -3;
    /// <summary>A finger that has been swallowed — the tap that dismissed the weapon panel.</summary>
    const int RoleNone = -4;

    /// <summary>
    /// Cells in the weapon grid, as 4 across by 3 down.
    ///
    /// Twelve because the rack shows ONE FAMILY at a time and the largest family
    /// is eight (GADGETS) — the tabs are what make sixty weapons fit on a phone.
    /// The four spare are headroom: a family that outgrew the grid would drop
    /// its last weapons silently, which is the kind of bug nobody reports
    /// because the gun that vanished is the one you never knew existed.
    /// </summary>
    const int WeaponCells = 12;
    const int WeaponCellColumns = 4;

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
    bool _forcedForRack;          // ...and whether it was the keyboard rack that forced it
    bool _rackWanted;             // rack asked for before the layout existed
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
    Button _fireLeft, _fireRight, _jump, _martial, _scope, _snipe, _prevWeapon, _nextWeapon, _menu, _rise, _sink, _shuffle;

    /// <summary>The form dial on TransformCast's canvas; tapping it morphs. See SetFormDial.</summary>
    static RectTransform _formDial;

    /// <summary>The four-gun bar along the bottom. See BuildShortcutBar.</summary>
    GameObject _barRoot;
    readonly List<Button> _shortcuts = new List<Button>();
    Text _shortcutHint;
    float _shortcutPulse;
    int _shortcutPick = -1;
    Button _weapons, _weaponClose, _weaponReset;

    RectTransform _weaponPanel;
    readonly List<Button> _weaponTabs = new List<Button>();
    readonly List<Button> _weaponCells = new List<Button>();
    /// <summary>Slot in the player's usable set behind each grid cell, or -1 for an empty one.</summary>
    readonly int[] _cellSlot = new int[WeaponCells];
    int _weaponTab;
    bool _weaponPanelOpen;
    int _weaponPick = -1;

    readonly Dictionary<int, int> _roles = new Dictionary<int, int>();
    readonly Dictionary<int, Vector2> _positions = new Dictionary<int, Vector2>();
    readonly List<Pointer> _pointers = new List<Pointer>();
    readonly List<int> _stale = new List<int>();

    int _stickFinger = int.MinValue;
    Vector2 _stickCenter;

    bool _jumpPressed;
    bool _martialPressed;
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
        /// <summary>The weapon's picture, on grid cells only.</summary>
        public RawImage icon;
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
        // The open weapon panel owns the whole screen, not just its rows.
        if (Instance._weaponPanelOpen)
            return true;
        foreach (var button in Instance._buttons)
            if (button.Visible &&
                RectTransformUtility.RectangleContainsScreenPoint(button.rect, screenPoint, null))
                return true;
        // The form dial is ours to answer for even though it is not ours to draw.
        return OverFormDial(screenPoint);
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
        // The rack's pictures are render textures — native memory, which does
        // not go with the managed objects that referenced it.
        WeaponIcons.Release();
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

    /// <summary>
    /// Slot tapped in the weapon panel, or -1. An absolute choice rather than a
    /// step, so the reader applies it instead of cycling.
    /// </summary>
    public int ConsumeWeaponPick()
    {
        int picked = _weaponPick;
        _weaponPick = -1;
        return picked;
    }

    /// <summary>Shortcut slot tapped on the bar, or -1.</summary>
    public int ConsumeShortcutPick()
    {
        int picked = _shortcutPick;
        _shortcutPick = -1;
        return picked;
    }

    /// <summary>True while the weapon panel is covering the screen.</summary>
    public static bool WeaponPanelOpen => Instance != null && Instance._weaponPanelOpen;

    /// <summary>
    /// Open or close the rack from the keyboard — the desktop half of the ARMS
    /// button.
    ///
    /// Desktop needs its own way in: the number keys reach the first nine
    /// weapons and the cycle arrows walk them one at a time, which is no way to
    /// find one gun in sixty. Rather than build a second rack for mouse and
    /// keyboard, this switches the on-screen controls on for as long as the rack
    /// is up — it is modal and covers the screen anyway, so the widgets behind
    /// it are never seen, and the mouse already stands in for a finger.
    ///
    /// The controls are only switched back off if WE turned them on: a player
    /// who pressed '=' to look at the phone layout keeps it after closing.
    /// </summary>
    public static void ToggleWeaponRack()
    {
        var controls = Ensure();
        if (controls._weaponPanelOpen || controls._rackWanted)
        {
            controls._rackWanted = false;
            controls.ToggleWeaponPanel(false);
            return;
        }

        controls._forcedForRack = !controls._forced;
        controls._forced = true;
        // Not opened here: the panel lives under a root that this frame may not
        // even have built yet. Update opens it once the layout is up.
        controls._rackWanted = true;
    }

    public bool ConsumeMorph()
    {
        bool pressed = _morphPressed;
        _morphPressed = false;
        return pressed;
    }

    /// <summary>
    /// Register the form dial — the top-right readout that says ROBOT or TANK —
    /// as the thing that morphs when tapped.
    ///
    /// WHY THE DIAL OWNS ITS OWN PIXELS BUT NOT ITS OWN TAPS. It has to be
    /// visible with the on-screen controls switched off, because in first person
    /// it is the only thing on screen that says which form you are in — so it
    /// lives on TransformCast's canvas, not this one. But a tap on it has to
    /// behave like a tap on any of our buttons: swallowed, so the same finger
    /// does not also start a look-drag, and reported by <see cref="PointOver"/>,
    /// so nothing underneath treats it as a click in the world. Registering the
    /// rect here is what buys both without moving the widget.
    /// </summary>
    public static void SetFormDial(RectTransform rect)
    {
        _formDial = rect;
    }

    /// <summary>Is a screen point on the form dial, and is the dial there to hit?</summary>
    static bool OverFormDial(Vector2 screenPoint) =>
        _formDial != null && _formDial.gameObject.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(_formDial, screenPoint, null);

    /// <summary>Sniper scope toggle edge — see SniperScope for why it isn't a hold.</summary>
    public bool ConsumeSnipe()
    {
        bool pressed = _snipePressed;
        _snipePressed = false;
        return pressed;
    }

    /// <summary>Martial-arts combo edge — the on-screen half of the F key.</summary>
    public bool ConsumeMartial()
    {
        bool pressed = _martialPressed;
        _martialPressed = false;
        return pressed;
    }

    void Update()
    {
        UpdateAvailability();

        // The shortcut bar is HUD rather than a control surface, so it is built
        // and painted OUTSIDE the active gate: keys 1-4 drive the same four guns
        // with the on-screen controls off, and the choose-your-four flow at the
        // top of a match is invisible without the slot that is glowing. Only its
        // taps need the controls on, and Assign is already behind that.
        EnsureShortcutBar();
        RefreshShortcuts();

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

        // Deferred from ToggleWeaponRack, which can be called on a frame where
        // there is no layout to open the rack over yet.
        if (_rackWanted)
        {
            _rackWanted = false;
            ToggleWeaponPanel(true);
        }

        GatherPointers();
        ProcessPointers();
        if (_weaponPanelOpen)
            RefreshWeaponPanel();
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
        _martialPressed = false;
        _morphPressed = false;
        _snipePressed = false;
        _weaponCycle = 0;
        _weaponPick = -1;
        _shortcutPick = -1;
        ToggleWeaponPanel(false);
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

        bool playing = GameModeController.IsFirstPersonMatch(mode);
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
        // Same rule as JUMP: a tank has no fists either.
        SetVisible(_martial, playing && canJump);
        SetVisible(_scope, playing);
        SetVisible(_snipe, playing);
        SetVisible(_prevWeapon, playing);
        SetVisible(_nextWeapon, playing);
        SetVisible(_weapons, playing);
        SetVisible(_rise, flying);
        SetVisible(_sink, flying);
        SetVisible(_shuffle, flying);
        SetVisible(_menu, true);

        if (!playing)
        {
            _jumpPressed = false;
            _martialPressed = false;
            _morphPressed = false;
            _snipePressed = false;
            _weaponCycle = 0;
            // Nothing to pick from outside a match, and a panel left open would
            // cover the fly-cam with a modal nobody can dismiss into anything.
            ToggleWeaponPanel(false);
            _weaponPick = -1;
            _shortcutPick = -1;
        }
        // A jump tapped in the instant before the fold started has nowhere to
        // land now; letting it sit would pop the robot the moment it unfolds.
        if (!canJump)
        {
            _jumpPressed = false;
            _martialPressed = false;
        }

        // There is no MORPH button any more. The form dial in the top-right is
        // the control — it already had to be on screen to say which form you are
        // in, and a separate button to change the thing it was showing was a
        // second widget for one idea. TransformCast draws it; SetFormDial is how
        // its taps get here.
    }

    static void SetVisible(Button button, bool visible)
    {
        if (button != null)
            SetVisible(button.rect, visible);
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
        // The weapon panel is modal: while it is up it is the only thing on
        // screen that can be pressed. Without this a tap meant for a row that
        // missed it would walk the camera, or worse, land on FIRE underneath.
        if (_weaponPanelOpen)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var candidate = _buttons[i];
                if (!IsWeaponPanelButton(candidate) || !candidate.Visible)
                    continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(candidate.rect, pointer.position, null))
                    continue;
                _roles[pointer.id] = i;
                OnButtonDown(candidate);
                return;
            }
            // The bar draws OVER this panel while the four are being chosen, so
            // it is the one thing on screen a thumb can land on that must not be
            // read as "done". Swallowed and ignored: the bar is a readout during
            // selection, not a control.
            foreach (var slot in _shortcuts)
            {
                if (!slot.Visible ||
                    !RectTransformUtility.RectangleContainsScreenPoint(slot.rect, pointer.position, null))
                    continue;
                _roles[pointer.id] = RoleNone;
                return;
            }

            // Tap anywhere else backs out. The finger is swallowed rather than
            // released into the world, so the dismissing tap can't also shoot.
            ToggleWeaponPanel(false);
            _roles[pointer.id] = RoleNone;
            return;
        }

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

        // The form dial. After our own buttons so nothing of ours can be
        // covered by it, and before the sticks so a tap on the top-right corner
        // morphs instead of starting a look-drag. Swallowed rather than given a
        // role: there is nothing to hold down, only a press.
        if (OverFormDial(pointer.position))
        {
            _morphPressed = true;
            _roles[pointer.id] = RoleNone;
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

    bool IsWeaponPanelButton(Button button) =>
        button != null && (button == _weaponClose || button == _weaponReset
                           || _weaponCells.Contains(button)
                           || _weaponTabs.Contains(button));

    void OnButtonDown(Button button)
    {
        int cell = _weaponCells.IndexOf(button);
        if (cell >= 0)
        {
            // An empty cell is a miss, not a dismissal: fat-thumbing the gap in
            // a short family should not close the rack you just opened.
            if (_cellSlot[cell] < 0)
                return;

            _weaponPick = _cellSlot[cell];

            // While the bar is still being built the rack STAYS UP. Choosing
            // four guns is four picks, and closing after each one would make it
            // four picks plus three trips back — with the bar drawn over the
            // backdrop, the player watches the slots fill and the glow step
            // along without ever leaving the screen they are choosing on.
            //
            // Read one pick ahead: PlayerBrain fills the slot on ITS update,
            // after this, so "will this be the last one" is the question, not
            // "was it".
            var bar = PlayerBrain.Local != null ? WeaponShortcuts.Of(PlayerBrain.Local) : null;
            bool stillChoosing = bar != null && WeaponLoadout.FullArsenalMatch
                                 && bar.NextEmpty >= 0 && bar.NextEmpty < WeaponShortcuts.SlotCount - 1;
            if (!stillChoosing)
                ToggleWeaponPanel(false);
            return;
        }

        int tab = _weaponTabs.IndexOf(button);
        if (tab >= 0)
        {
            _weaponTab = tab;
            RefreshWeaponPanel();
            return;
        }

        int shortcut = _shortcuts.IndexOf(button);
        if (shortcut >= 0)
        {
            var shortcuts = PlayerBrain.Local != null
                ? WeaponShortcuts.Of(PlayerBrain.Local) : null;
            var weapon = shortcuts != null ? shortcuts.Get(shortcut) : null;
            if (weapon != null)
                _shortcutPick = shortcut;
            else
                // An empty slot IS the way into the rack. The player who taps
                // the glowing circle is asking the only question it can answer.
                ToggleWeaponPanel(true);
            return;
        }

        // Only ever opens: while the panel is up it swallows every tap outside
        // itself, ARMS included, so backing out goes through CLOSE or the
        // backdrop rather than through this button a second time.
        if (button == _weapons) ToggleWeaponPanel(true);
        else if (button == _weaponClose) ToggleWeaponPanel(false);
        else if (button == _weaponReset)
        {
            var bar = PlayerBrain.Local != null ? WeaponShortcuts.Of(PlayerBrain.Local) : null;
            if (bar != null)
                bar.Clear();
            // Stays open: the player who just emptied the bar is here to fill
            // it, and closing on them would only cost a tap to come back.
            RefreshWeaponPanel();
        }
        else if (button == _jump) _jumpPressed = true;
        else if (button == _martial) _martialPressed = true;
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
            // The rack paints itself — cells in their gun's own colour, tabs by
            // which one is open — and the standard idle/held wash would just
            // erase that every frame.
            if (_weaponCells.Contains(button) || _weaponTabs.Contains(button)
                || _shortcuts.Contains(button))
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
        // Between FIRE and ARMS, clear of the aim stick's 200-unit grab zone.
        _martial = MakeRoundButton("Martial", "MARTIAL\nARTS", new Vector2(1, 0), new Vector2(-560, 480), 150);
        _scope = MakeRoundButton("Scope", "X-RAY", new Vector2(1, 0), new Vector2(-175, 470), 140);
        _snipe = MakeRoundButton("Snipe", "SNIPE", new Vector2(1, 0), new Vector2(-175, 630), 140);
        _prevWeapon = MakeRoundButton("PrevWeapon", "<", new Vector2(1, 0), new Vector2(-700, 150), 110);
        _nextWeapon = MakeRoundButton("NextWeapon", ">", new Vector2(1, 0), new Vector2(-570, 150), 110);
        // Sits directly above the two arrows it supersedes: they step one slot
        // blind, this shows the whole rack and lets a thumb land on a name.
        _weapons = MakeRoundButton("Weapons", "ARMS", new Vector2(1, 0), new Vector2(-635, 290), 140);

        // Arena Builder fly-cam extras.
        _rise = MakeRoundButton("Rise", "UP", new Vector2(1, 0), new Vector2(-170, 330), 150);
        _sink = MakeRoundButton("Sink", "DOWN", new Vector2(1, 0), new Vector2(-370, 180), 150);
        _shuffle = MakeRoundButton("Shuffle", "NEW", new Vector2(1, 0), new Vector2(-170, 520), 150);

        // No Escape key on a tablet — this is the only way back to the menu.
        // Top-LEFT, where Roblox puts it, which also leaves the opposite corner
        // free for the transformation replay (TransformCast).
        _menu = MakeRoundButton("Menu", "MENU", new Vector2(0, 1), new Vector2(130, -110), 150);

        BuildWeaponPanel();
    }

    /// <summary>
    /// The shortcut bar: four guns along the bottom of the screen, one tap each.
    ///
    /// Bottom CENTRE, which is the one part of the layout nothing else wanted —
    /// the move stick owns the lower left, the aim stick and the action buttons
    /// the lower right, and a bar under the crosshair is where every shooter has
    /// put its weapon slots for thirty years.
    ///
    /// Empty at the start of a match and filled by choosing from the rack. The
    /// slot waiting to be filled pulses; see <see cref="RefreshShortcuts"/> for
    /// why that pulse is the entire instruction manual for the feature.
    /// </summary>
    void EnsureShortcutBar()
    {
        if (_barRoot != null)
            return;

        // Its OWN canvas, not the control root's: that one is switched off the
        // moment the on-screen controls hand back to mouse and keyboard, and
        // this has to stay readable there.
        //
        // ABOVE the on-screen controls (15) rather than below, because the rack
        // stays open while the four are being chosen and the whole point is
        // watching the slots fill through it. Still under the menu (20). It sits
        // at the bottom centre where none of the controls are, so drawing over
        // them costs nothing.
        _barRoot = new GameObject("TouchShortcutBar");
        _barRoot.transform.SetParent(transform, false);
        var canvas = _barRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var scaler = _barRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        var barRect = _barRoot.GetComponent<RectTransform>();

        for (int i = 0; i < WeaponShortcuts.SlotCount; i++)
        {
            var slot = MakeRoundButton($"Shortcut{i + 1}", "", new Vector2(0.5f, 0f),
                new Vector2((i - (WeaponShortcuts.SlotCount - 1) * 0.5f) * 132f, 118f), 118f,
                barRect);

            // Under the label so the number still reads over a dark gun.
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(slot.rect, false);
            iconGo.transform.SetSiblingIndex(1);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            // Inset so the round rim stays a rim rather than a frame around a
            // square picture butting up against it.
            iconRect.offsetMin = new Vector2(11f, 11f);
            iconRect.offsetMax = new Vector2(-11f, -11f);
            slot.icon = icon;

            // The key that does the same thing, tucked into the corner. Small on
            // purpose: it is a footnote for the keyboard, not the label.
            var key = MakeLabel(slot.rect, "Key", (i + 1).ToString(), 18,
                Vector2.zero, Vector2.zero);
            key.alignment = TextAnchor.LowerRight;
            var keyRect = key.rectTransform;
            keyRect.anchorMin = Vector2.zero;
            keyRect.anchorMax = Vector2.one;
            keyRect.offsetMin = new Vector2(0f, 6f);
            keyRect.offsetMax = new Vector2(-12f, 0f);

            _shortcuts.Add(slot);
        }

        // One line of instruction, and only while it is true.
        _shortcutHint = MakeLabel(barRect, "ShortcutHint", "", 26,
            new Vector2(0f, 200f), new Vector2(900f, 34f));
        _shortcutHint.color = new Color(1f, 1f, 1f, 0.65f);
        var hintRect = _shortcutHint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
    }

    /// <summary>
    /// Paint the bar: the guns that have been chosen, and the slot waiting for
    /// the next one.
    ///
    /// THE PULSE IS THE INSTRUCTION. Nothing tells the player "now pick four
    /// weapons" — one slot glows, they open the rack, they tap a gun, the glow
    /// moves along. The rule is learned by watching it happen once, which is the
    /// only kind of tutorial that survives a seven-year-old.
    /// </summary>
    void RefreshShortcuts()
    {
        var player = PlayerBrain.Local;
        var shortcuts = player != null ? WeaponShortcuts.Of(player) : null;

        // Only where the whole catalogue is carried. A bar of four is the answer
        // to sixty weapons; against the two basics and a pod every other mode
        // hands out it is four slots that can never all be filled, and a hint
        // that would pulse "2 MORE" for the rest of the match.
        if (!WeaponLoadout.FullArsenalMatch)
            shortcuts = null;

        if (shortcuts == null)
        {
            foreach (var slot in _shortcuts)
                SetVisible(slot, false);
            SetVisible(_shortcutHint != null ? _shortcutHint.rectTransform : null, false);
            return;
        }

        int next = shortcuts.NextEmpty;
        var held = player.ActiveWeapon();
        _shortcutPulse = Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);

        for (int i = 0; i < _shortcuts.Count; i++)
        {
            var slot = _shortcuts[i];
            SetVisible(slot, true);

            var weapon = shortcuts.Get(i);
            slot.icon.texture = weapon != null ? WeaponIcons.IconFor(weapon) : null;
            slot.icon.enabled = slot.icon.texture != null;
            slot.label.text = weapon != null ? "" : "+";

            bool waiting = i == next;
            Color tint = weapon != null ? weapon.color : HoloCyan;

            // Three states, three fills: waiting (breathing), holding this gun
            // (its own colour), and everything else (the standard idle wash).
            slot.image.color = slot.held
                ? new Color(tint.r, tint.g, tint.b, 0.85f)
                : waiting
                    ? Color.Lerp(ButtonIdle, new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.55f),
                                 _shortcutPulse)
                    : weapon != null && weapon == held
                        ? new Color(tint.r, tint.g, tint.b, 0.5f)
                        : ButtonIdle;
            slot.rim.color = new Color(tint.r, tint.g, tint.b,
                waiting ? Mathf.Lerp(0.5f, 1f, _shortcutPulse)
                        : weapon != null && weapon == held ? 0.95f : 0.45f);
            slot.label.color = new Color(1f, 1f, 1f, waiting ? 0.9f : 0.5f);
        }

        bool choosing = shortcuts.Choosing;
        SetVisible(_shortcutHint.rectTransform, choosing);
        if (choosing)
            _shortcutHint.text = _weaponPanelOpen
                ? $"TAP  A  WEAPON  —  {WeaponShortcuts.SlotCount - next}  TO  GO"
                : next == 0
                    ? "CHOOSE  4  WEAPONS  —  OPEN  ARMS"
                    : $"{WeaponShortcuts.SlotCount - next}  MORE";
    }

    /// <summary>
    /// The weapon rack: what this robot can shoot right now, as a tab per family
    /// and a grid of pictures.
    ///
    /// WHY TABS. Player v AI arms everyone with the whole catalogue — sixty
    /// weapons — and a flat list of sixty is not a rack, it is a spreadsheet.
    /// The families were already there as headings in WeaponCatalog, and they
    /// are how a player thinks about the arsenal: you go looking for "something
    /// icy", not for weapon forty-one.
    ///
    /// WHY PICTURES. The names alone are no help the first time you meet them —
    /// nothing in "Glowworm Launcher" tells a seven-year-old what comes out of
    /// it. The picture is the weapon itself, rendered from the same prop the
    /// hands hold (see WeaponIcons), so what you pick is what you get.
    ///
    /// Built once and hidden, never built on demand — the tabs and cells are
    /// registered in <see cref="_buttons"/> and fingers hold an INDEX into that
    /// list, so a panel that added and removed them would renumber the buttons
    /// under a thumb that was already down on one.
    /// </summary>
    void BuildWeaponPanel()
    {
        var panel = new GameObject("WeaponPanel");
        panel.transform.SetParent(_root.transform, false);
        _weaponPanel = panel.AddComponent<RectTransform>();
        _weaponPanel.anchorMin = Vector2.zero;
        _weaponPanel.anchorMax = Vector2.one;
        _weaponPanel.offsetMin = Vector2.zero;
        _weaponPanel.offsetMax = Vector2.zero;

        // Dims the fight behind the rack, and gives the "tap anywhere to back
        // out" gesture something to look like.
        var backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(_weaponPanel, false);
        var backdropImage = backdrop.AddComponent<Image>();
        backdropImage.color = new Color(0.02f, 0.05f, 0.09f, 0.82f);
        backdropImage.raycastTarget = false;
        var backdropRect = backdropImage.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        MakeLabel(_weaponPanel, "Title", "WEAPONS", 46, new Vector2(0f, 450f), new Vector2(900f, 62f));

        // One tab per family, laid out across the top. Built from the catalogue
        // rather than a list here, so a new family shows up in the rack the day
        // it is added and never has to be registered twice.
        var tabs = WeaponCatalog.Tabs;
        float tabSpan = Mathf.Min(1720f, tabs.Length * 158f);
        float tabStep = tabs.Length > 1 ? tabSpan / tabs.Length : 0f;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = MakeTabButton($"WeaponTab{i}", tabs[i].name, _weaponPanel);
            tab.rect.anchoredPosition =
                new Vector2((i - (tabs.Length - 1) * 0.5f) * tabStep, 360f);
            tab.rect.sizeDelta = new Vector2(tabStep - 10f, 62f);
            _weaponTabs.Add(tab);
        }

        for (int i = 0; i < WeaponCells; i++)
        {
            var cell = MakeWeaponCell($"WeaponCell{i}", _weaponPanel);
            int column = i % WeaponCellColumns;
            int row = i / WeaponCellColumns;
            cell.rect.anchoredPosition = new Vector2(
                (column - (WeaponCellColumns - 1) * 0.5f) * 320f,
                175f - row * 230f);
            _weaponCells.Add(cell);
        }

        _weaponClose = MakeRoundButton("WeaponClose", "CLOSE", new Vector2(0.5f, 0.5f),
            new Vector2(0f, -465f), 130);
        // Round buttons parent themselves to the canvas; this one belongs to the
        // panel, so it hides and shows with it.
        _weaponClose.rect.SetParent(_weaponPanel, false);

        // The only way to change a choice, and it changes all four: reassigning
        // one slot needs the player to say WHICH slot, and not having to say
        // that is the whole reason the bar fills in order. Redoing four picks
        // costs four taps, which is cheaper than the interface that would let
        // you redo one.
        _weaponReset = MakeRoundButton("WeaponReset", "REDO  4", new Vector2(0.5f, 0.5f),
            new Vector2(230f, -465f), 130);
        _weaponReset.rect.SetParent(_weaponPanel, false);

        _weaponPanel.gameObject.SetActive(false);
    }

    /// <summary>One family tab across the top of the rack.</summary>
    Button MakeTabButton(string name, string caption, RectTransform parent)
    {
        var go = new GameObject($"Touch_{name}");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = ButtonIdle;
        image.raycastTarget = false;

        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var rim = new GameObject("Rim");
        rim.transform.SetParent(rect, false);
        var rimImage = rim.AddComponent<Image>();
        rimImage.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.5f);
        rimImage.raycastTarget = false;
        var rimRect = rimImage.rectTransform;
        // A four-pixel underline rather than a border: the project has no
        // nine-sliced frame sprite, and a stretched ring reads as an ellipse.
        // On a tab it doubles as the "you are here" mark.
        rimRect.anchorMin = new Vector2(0f, 0f);
        rimRect.anchorMax = new Vector2(1f, 0f);
        rimRect.offsetMin = Vector2.zero;
        rimRect.offsetMax = new Vector2(0f, 4f);

        var text = MakeLabel(rect, "Label", caption, 22, Vector2.zero, Vector2.zero);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 0f);
        textRect.offsetMax = new Vector2(-6f, 0f);

        var button = new Button { rect = rect, image = image, rim = rimImage, label = text };
        _buttons.Add(button);
        return button;
    }

    /// <summary>
    /// One weapon in the grid: its picture, its name under it, and its slot
    /// number in the corner so the keyboard shortcut and the rack agree.
    /// </summary>
    Button MakeWeaponCell(string name, RectTransform parent)
    {
        var go = new GameObject($"Touch_{name}");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = ButtonIdle;
        image.raycastTarget = false;

        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(296f, 210f);

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(rect, false);
        var icon = iconGo.AddComponent<RawImage>();
        icon.raycastTarget = false;
        var iconRect = icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -8f);
        iconRect.sizeDelta = new Vector2(140f, 140f);

        var rim = new GameObject("Rim");
        rim.transform.SetParent(rect, false);
        var rimImage = rim.AddComponent<Image>();
        rimImage.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b, 0.5f);
        rimImage.raycastTarget = false;
        var rimRect = rimImage.rectTransform;
        rimRect.anchorMin = new Vector2(0f, 0f);
        rimRect.anchorMax = new Vector2(1f, 0f);
        rimRect.offsetMin = Vector2.zero;
        rimRect.offsetMax = new Vector2(0f, 4f);

        // Two lines of room: several weapons have names that will not fit a
        // 296-wide cell on one, and a clipped name is no name at all.
        var text = MakeLabel(rect, "Label", "", 21, Vector2.zero, Vector2.zero);
        var textRect = text.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 0f);
        textRect.pivot = new Vector2(0.5f, 0f);
        textRect.offsetMin = new Vector2(8f, 8f);
        textRect.offsetMax = new Vector2(-8f, 60f);

        var button = new Button
        {
            rect = rect, image = image, rim = rimImage, label = text, icon = icon,
        };
        _buttons.Add(button);
        return button;
    }

    Text MakeLabel(RectTransform parent, string name, string content, int fontSize,
                   Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return text;
    }

    void ToggleWeaponPanel(bool open)
    {
        if (_weaponPanelOpen == open)
            return;
        _weaponPanelOpen = open;
        if (_weaponPanel != null)
            _weaponPanel.gameObject.SetActive(open);
        if (open)
        {
            RefreshWeaponPanel();
            return;
        }

        // Hand the screen back to mouse and keyboard if the rack was the only
        // reason the on-screen controls were up. Also covers ClearState closing
        // it on the way out of a match.
        if (_forcedForRack)
        {
            _forcedForRack = false;
            _forced = false;
        }
        _rackWanted = false;
    }

    /// <summary>
    /// Re-read the usable set into the tabs and the grid. Runs every frame the
    /// panel is up, not just on open: an airdropped weapon is on a countdown,
    /// and it can expire out of the rack while the player is looking straight
    /// at it.
    /// </summary>
    void RefreshWeaponPanel()
    {
        var player = PlayerBrain.Local;
        var carried = player != null ? player.weapons : null;
        if (carried == null || carried.Length == 0)
        {
            ToggleWeaponPanel(false);
            return;
        }

        var tabs = WeaponCatalog.Tabs;
        var loadout = player.GetComponent<WeaponLoadout>();
        var bar = WeaponShortcuts.Of(player);
        int active = player.ActiveSlot;

        // A tab with nothing behind it is dimmed rather than hidden: which
        // families exist is worth knowing even in a mode that only hands out
        // two guns, and a strip that changes length between modes is harder to
        // learn than one that greys out.
        //
        // The open tab is corrected FIRST, so the grid below is never filled
        // from a tab that has since emptied — which is what happens when the
        // airdropped weapon behind the open tab expires.
        int chosen = _weaponTab;
        if (!TabHasWeapons(carried, chosen))
        {
            chosen = 0;
            for (int t = 0; t < tabs.Length; t++)
                if (TabHasWeapons(carried, t)) { chosen = t; break; }
            _weaponTab = chosen;
        }

        for (int t = 0; t < _weaponTabs.Count; t++)
        {
            var tab = _weaponTabs[t];
            bool populated = TabHasWeapons(carried, t);
            bool open = t == chosen;
            tab.dimmed = !populated;
            tab.image.color = open ? new Color(HoloCyan.r * 0.35f, HoloCyan.g * 0.35f, HoloCyan.b * 0.35f, 0.95f)
                                   : ButtonIdle;
            tab.rim.color = new Color(HoloCyan.r, HoloCyan.g, HoloCyan.b,
                                      open ? 0.95f : populated ? 0.4f : 0.12f);
            tab.label.color = open ? HoloCyan
                                   : new Color(1f, 1f, 1f, populated ? 0.75f : 0.3f);
        }

        int next = 0;
        for (int slot = 0; slot < carried.Length && next < _weaponCells.Count; slot++)
        {
            if (TabOf(carried[slot]) != chosen)
                continue;

            var cell = _weaponCells[next];
            _cellSlot[next] = slot;
            next++;

            var weapon = carried[slot];
            SetVisible(cell.rect, true);
            cell.icon.texture = WeaponIcons.IconFor(weapon);
            cell.icon.enabled = cell.icon.texture != null;

            string name = weapon != null ? weapon.weaponName.ToUpperInvariant() : "EMPTY";
            // The airdropped slot is the only one that can vanish, so it is the
            // only one that says how long it has left.
            string tail = "";
            if (loadout != null && loadout.Special != null && weapon == loadout.Special)
                tail = $"\n{Mathf.CeilToInt(loadout.SpecialSecondsLeft)}s";
            // A gun already on the bar says which key it is under. Picking it
            // again is a no-op by design — Assign refuses duplicates — and a
            // tap that appears to do nothing is only baffling if nothing on the
            // card explains why.
            int barSlot = bar != null ? bar.SlotOf(weapon) : -1;
            cell.label.text = barSlot >= 0 ? $"{barSlot + 1}  ·  {name}{tail}" : $"{name}{tail}";

            // The held weapon reads as held: its own colour on the cell, rather
            // than a tick somewhere that a thumb would cover.
            cell.dimmed = false;
            Color tint = weapon != null ? weapon.color : HoloCyan;
            cell.image.color = cell.held
                ? new Color(tint.r, tint.g, tint.b, 0.85f)
                : slot == active
                    ? new Color(tint.r, tint.g, tint.b, 0.55f)
                    : ButtonIdle;
            cell.rim.color = new Color(tint.r, tint.g, tint.b, slot == active ? 0.95f : 0.45f);
            cell.label.color = Color.white;
        }

        for (int i = next; i < _weaponCells.Count; i++)
        {
            _cellSlot[i] = -1;
            SetVisible(_weaponCells[i].rect, false);
        }
    }

    /// <summary>
    /// Which tab a carried weapon belongs under. Weapons the catalogue does not
    /// know — nothing carries one today, but the vehicle siege kit is exactly
    /// that shape — fall into the core tab rather than out of the rack.
    /// </summary>
    static int TabOf(Weapon weapon)
    {
        var group = WeaponCatalog.GroupOf(weapon);
        if (group == null)
            return 0;
        var tabs = WeaponCatalog.Tabs;
        for (int i = 0; i < tabs.Length; i++)
            if (tabs[i] == group)
                return i;
        return 0;
    }

    static bool TabHasWeapons(Weapon[] carried, int tab)
    {
        foreach (var weapon in carried)
            if (weapon != null && TabOf(weapon) == tab)
                return true;
        return false;
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

    Button MakeRoundButton(string name, string label, Vector2 anchor, Vector2 position, float size,
                           Transform parent = null)
    {
        var go = new GameObject($"Touch_{name}");
        go.transform.SetParent(parent != null ? parent : _root.transform, false);
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

    /// <summary>
    /// The round-button disc. Internal because the form dial is drawn on
    /// TransformCast's canvas and has to look like it belongs to this set —
    /// "like the other buttons" is the whole point of it, and two hand-rolled
    /// circles would drift apart.
    /// </summary>
    internal static Sprite DiscSprite()
    {
        if (_disc == null) _disc = BuildCircle(0f);
        return _disc;
    }

    /// <summary>The round-button rim. Internal for the same reason as the disc.</summary>
    internal static Sprite RingSprite()
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
