using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tablet hands for DOGFIGHT: a dynamic stick under the left thumb that
/// steers whatever form the pawn is wearing, a dynamic stick under the right
/// that throttles a jet and aims a robot or tank, and a fixed column of
/// buttons on the right edge for the one-tap verbs — missile, flares,
/// transform, camera. No fire button: on glass the guns fire themselves, the
/// TANK RAID rule, because a thumb that must hold FIRE is a thumb that
/// cannot aim.
///
/// RAW TOUCHES, NOT uGUI — TankSticks' two reasons, unchanged: this needs
/// several fingers read independently every frame, and TouchControls turns
/// <c>Input.simulateMouseWithTouches</c> off while on-screen controls are up,
/// so uGUI would receive nothing at all. The buttons are hand-hit-tested in
/// the same pointer loop for the same reason, and claim their touches BEFORE
/// the sticks do, or every tap on MISSILE would also plant an aim stick
/// under it.
///
/// Appears whenever <see cref="TouchControls"/> says controls are up — a real
/// touch, or '=' on any machine (its own testing toggle). The mouse stands in
/// for one finger so the whole layout can be driven in the editor.
/// </summary>
/// <remarks>Runs BEFORE the mode (which runs at -50): the mode consumes this
/// frame's taps, and a tap consumed one frame late is a missile fired after
/// the lock drifted off.</remarks>
[DefaultExecutionOrder(-60)]
public class DogfightSticks : MonoBehaviour
{
    /// <summary>Left thumb: steer / move, -1..1 per axis.</summary>
    public static Vector2 Steer { get; private set; }

    /// <summary>Right thumb: throttle (jets read y) or aim (ground forms
    /// read both). Zero while the thumb is off it.</summary>
    public static Vector2 AimStick { get; private set; }

    /// <summary>True while the sticks own input — the mode falls back to
    /// keyboard and mouse otherwise.</summary>
    public static bool Showing { get; private set; }

    // One-tap verbs, true for exactly the frame they were tapped. The mode
    // runs after this (see the execution order note), so same-frame reads.
    public static bool MissileTapped { get; private set; }
    public static bool FlaresTapped { get; private set; }
    public static bool TransformTapped { get; private set; }
    public static bool CameraTapped { get; private set; }
    public static bool JumpTapped { get; private set; }

    /// <summary>A thumb is down on either FIRE button — the deck's manual
    /// trigger. Only ever true in the ground layout; in the air the guns
    /// still fire themselves.</summary>
    public static bool GunHeld { get; private set; }

    /// <summary>
    /// Asserted by the mode every frame, the WeaponsFree way (statics die in
    /// a mid-Play recompile): while the hero stands on the deck as a robot or
    /// tank, the glass folds into the GUNFIGHT game's hand — the aim stick
    /// fixed at TouchControls' spot, twin held FIRE buttons over each thumb,
    /// and JUMP where the gunfight puts it (robots only; a tank has no legs).
    /// </summary>
    public static void SetGroundLayout(bool ground, bool canJump)
    {
        _groundWanted = ground;
        _jumpWanted = canJump;
    }

    static bool _groundWanted;
    static bool _jumpWanted;

    /// <summary>A tap on the WORLD — a touch that ended quickly without ever
    /// really moving, anywhere that is not a button. The mode spends it on
    /// target locking. Valid for exactly one frame.</summary>
    public static bool WorldTapped { get; private set; }
    public static Vector2 WorldTapPoint { get; private set; }

    /// <summary>A touch older or more travelled than this is steering, not
    /// tapping.</summary>
    const float TapSeconds = 0.28f;
    const float TapDriftPixels = 24f;

    const float Radius = 145f;
    const float DeadZone = 0.16f;
    const int MouseId = -1971;

    const float ButtonSize = 92f;

    static readonly Color RingIdle = new Color(0.2f, 0.9f, 1f, 0.30f);
    static readonly Color KnobIdle = new Color(0.2f, 0.9f, 1f, 0.55f);
    static readonly Color RingAim = new Color(1f, 0.72f, 0.25f, 0.30f);
    static readonly Color KnobAim = new Color(1f, 0.72f, 0.25f, 0.6f);
    static readonly Color ButtonFace = new Color(0.2f, 0.9f, 1f, 0.22f);
    static readonly Color ButtonHot = new Color(1f, 0.72f, 0.25f, 0.55f);

    class Stick
    {
        public RectTransform baseRect;
        public RectTransform knob;
        public Color ringColor;
        public Color knobColor;
        public int finger = int.MinValue;
        public Vector2 center;
        public Vector2 value;
        public float fade;
        public float claimedAt;
        public Vector2 claimedScreen;
        public Vector2 lastScreen;
        public bool travelled;
        public bool Held => finger != int.MinValue;
    }

    class TapButton
    {
        public RectTransform rect;
        public Image face;
        public Text label;
        public float flash;
        public System.Action tapped;
        /// <summary>Held rather than tapped (the FIRE pair): the finger that
        /// landed on it is this button's until it lifts.</summary>
        public bool hold;
        public int finger = int.MinValue;
        /// <summary>Resting spot, so a layout that slides the button can put
        /// it back.</summary>
        public Vector2 home;
        public bool Held => finger != int.MinValue;
    }

    /// <summary>A touch neither stick wanted (the fixed ground-layout aim
    /// stick refuses grabs far from its ring) — tracked only so a quick,
    /// still touch can still become a WORLD tap for target locking.</summary>
    class FreeTouch
    {
        public int id;
        public float at;
        public Vector2 start;
        public bool travelled;
    }

    GameObject _canvas;
    RectTransform _canvasRect;
    Stick _left, _right;
    readonly List<TapButton> _buttons = new List<TapButton>();
    TapButton _missile, _fireLeft, _fireRight, _jump;
    /// <summary>The right-edge verb column, which slides up one slot in the
    /// ground layout to clear JUMP at the gunfight's own spot.</summary>
    readonly List<TapButton> _column = new List<TapButton>();
    bool _ground;
    readonly List<FreeTouch> _free = new List<FreeTouch>();
    static Sprite _disc, _ring;

    readonly List<int> _seen = new List<int>();

    public static DogfightSticks Build(Transform parent)
    {
        var go = new GameObject("DogfightSticks");
        go.transform.SetParent(parent, false);
        return go.AddComponent<DogfightSticks>();
    }

    void OnDestroy()
    {
        Steer = AimStick = Vector2.zero;
        Showing = false;
        MissileTapped = FlaresTapped = TransformTapped = CameraTapped = false;
        JumpTapped = false;
        GunHeld = false;
    }

    void Update()
    {
        MissileTapped = FlaresTapped = TransformTapped = CameraTapped = false;
        JumpTapped = false;
        WorldTapped = false;

        bool wanted = TouchControls.Active;
        if (_canvas == null && wanted)
            Compose();
        if (_canvas != null && _canvas.activeSelf != wanted)
            _canvas.SetActive(wanted);

        Showing = wanted;
        if (!wanted)
        {
            Release(_left);
            Release(_right);
            foreach (var button in _buttons)
                button.finger = int.MinValue;
            _free.Clear();
            Steer = AimStick = Vector2.zero;
            GunHeld = false;
            return;
        }

        ApplyGroundLayout();
        ReadPointers();
        Steer = _left.value;
        AimStick = _right.value;
        GunHeld = _ground && (_fireLeft.Held || _fireRight.Held);
        Paint(_left);
        // In the ground layout the aim ring is a fixed target the thumb
        // returns to blind, so it stays visible even with no finger on it.
        Paint(_right, _ground);
        foreach (var button in _buttons)
        {
            button.flash = Mathf.MoveTowards(button.flash, 0f, Time.deltaTime * 3.5f);
            float hot = button.hold && button.Held ? 1f : button.flash;
            button.face.color = Color.Lerp(ButtonFace, ButtonHot, hot);
        }
    }

    /// <summary>
    /// Fold the layout to match the hero's footing. Runs every frame: the
    /// fixed aim centre depends on the live canvas size, and the mode's
    /// wanted-state is a static that must be re-read anyway.
    /// </summary>
    void ApplyGroundLayout()
    {
        _ground = _groundWanted;
        SetButtonActive(_fireLeft, _ground);
        SetButtonActive(_fireRight, _ground);
        SetButtonActive(_jump, _ground && _jumpWanted);
        // On the deck the twin FIRE buttons are the guns, so the verb column's
        // missile button says what it always meant.
        if (_missile.label != null)
            _missile.label.text = _ground ? "MISSILE" : "FIRE";
        // And the column steps up one slot so JUMP fits beneath it.
        float lift = _ground ? ButtonSize + 16f : 0f;
        foreach (var verb in _column)
            verb.rect.anchoredPosition = verb.home + Vector2.up * lift;
        if (!_ground)
            return;

        // TouchControls' aim-stick corner, translated into this canvas's
        // centre-based units.
        Vector2 half = _canvasRect.rect.size * 0.5f;
        var center = new Vector2(half.x - 370f, -half.y + 280f);
        if (!_right.Held && _right.center != center)
        {
            _right.center = center;
            _right.baseRect.anchoredPosition = center;
            _right.knob.anchoredPosition = center;
        }
    }

    static void SetButtonActive(TapButton button, bool active)
    {
        if (button == null)
            return;
        if (!active)
            button.finger = int.MinValue;
        if (button.rect.gameObject.activeSelf != active)
            button.rect.gameObject.SetActive(active);
    }

    void ReadPointers()
    {
        _seen.Clear();

        for (int i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            bool ending = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            Feed(touch.fingerId, touch.position, touch.phase == TouchPhase.Began, ending);
            if (!ending)
                _seen.Add(touch.fingerId);
        }

        if (Input.touchCount == 0 && Input.GetMouseButton(0))
        {
            Feed(MouseId, Input.mousePosition, Input.GetMouseButtonDown(0), false);
            _seen.Add(MouseId);
        }

        if (_left.Held && !_seen.Contains(_left.finger)) Release(_left);
        if (_right.Held && !_seen.Contains(_right.finger)) Release(_right);
        foreach (var button in _buttons)
            if (button.Held && !_seen.Contains(button.finger))
                button.finger = int.MinValue;
        _free.RemoveAll(touch => !_seen.Contains(touch.id));
    }

    void Feed(int id, Vector2 screenPoint, bool began, bool ended)
    {
        // A finger parked on a held button is that button's until it lifts.
        foreach (var button in _buttons)
            if (button.finger == id)
            {
                if (ended)
                    button.finger = int.MinValue;
                return;
            }

        var stick = _left.finger == id ? _left : _right.finger == id ? _right : null;

        if (stick == null)
        {
            if (!began)
            {
                FeedFree(id, screenPoint, ended);
                return;
            }
            // Buttons and the MENU corner claim first — a tap must never
            // double as a planted stick.
            if (TouchControls.PointOver(screenPoint))
                return;
            if (TapButtonAt(id, screenPoint))
                return;
            stick = screenPoint.x < Screen.width * 0.5f ? _left : _right;
            // In the ground layout the aim stick is FIXED, the gunfight rule:
            // only a grab near its ring steers (a little outside still counts
            // — mid-fight thumbs are not precise), and the centre never moves
            // to the thumb. Everything the sticks refuse falls through to the
            // free-touch tracker so a tap can still lock a target.
            bool fixedStick = _ground && stick == _right;
            if (stick.Held
                || (fixedStick && (ToCanvas(screenPoint) - stick.center).magnitude > Radius * 1.35f))
            {
                _free.Add(new FreeTouch
                {
                    id = id,
                    at = Time.unscaledTime,
                    start = screenPoint,
                });
                return;
            }
            stick.finger = id;
            if (!fixedStick)
            {
                stick.center = ToCanvas(screenPoint);
                stick.baseRect.anchoredPosition = stick.center;
            }
            stick.claimedAt = Time.unscaledTime;
            stick.claimedScreen = screenPoint;
            stick.travelled = false;
        }

        stick.lastScreen = screenPoint;
        float drift = TapDriftPixels * (Screen.height / 1080f);
        if ((screenPoint - stick.claimedScreen).sqrMagnitude > drift * drift)
            stick.travelled = true;

        if (ended)
        {
            // A touch that came and went without travelling was never
            // steering — it was pointing AT something. Hand it to the mode.
            if (!stick.travelled && Time.unscaledTime - stick.claimedAt < TapSeconds)
            {
                WorldTapped = true;
                WorldTapPoint = stick.lastScreen;
            }
            Release(stick);
            return;
        }

        Vector2 offset = ToCanvas(screenPoint) - stick.center;
        Vector2 clamped = Vector2.ClampMagnitude(offset, Radius);
        stick.knob.anchoredPosition = stick.center + clamped;

        Vector2 raw = clamped / Radius;
        float magnitude = raw.magnitude;
        stick.value = magnitude <= DeadZone
            ? Vector2.zero
            : raw.normalized * ((magnitude - DeadZone) / (1f - DeadZone));
    }

    bool TapButtonAt(int id, Vector2 screenPoint)
    {
        foreach (var button in _buttons)
        {
            if (!button.rect.gameObject.activeSelf)
                continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(button.rect, screenPoint))
                continue;
            if (button.hold)
            {
                button.finger = id;
            }
            else
            {
                button.flash = 1f;
                button.tapped();
            }
            return true;
        }
        return false;
    }

    /// <summary>Ride an unclaimed touch to its end; a quick, still one is a
    /// WORLD tap, same test the sticks apply to their own.</summary>
    void FeedFree(int id, Vector2 screenPoint, bool ended)
    {
        foreach (var touch in _free)
        {
            if (touch.id != id)
                continue;
            float drift = TapDriftPixels * (Screen.height / 1080f);
            if ((screenPoint - touch.start).sqrMagnitude > drift * drift)
                touch.travelled = true;
            if (ended)
            {
                if (!touch.travelled && Time.unscaledTime - touch.at < TapSeconds)
                {
                    WorldTapped = true;
                    WorldTapPoint = screenPoint;
                }
                _free.Remove(touch);
            }
            return;
        }
    }

    void Release(Stick stick)
    {
        if (stick == null)
            return;
        stick.finger = int.MinValue;
        stick.value = Vector2.zero;
        if (stick.knob != null)
            stick.knob.anchoredPosition = stick.center;
    }

    Vector2 ToCanvas(Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, screenPoint, null, out var local);
        return local;
    }

    void Paint(Stick stick, bool persistent = false)
    {
        stick.fade = Mathf.MoveTowards(stick.fade,
            stick.Held || persistent ? 1f : 0f, Time.deltaTime * 6f);
        stick.baseRect.GetComponent<Image>().color = Fade(stick.ringColor, stick.ringColor.a * stick.fade);
        stick.knob.GetComponent<Image>().color = Fade(stick.knobColor, stick.knobColor.a * stick.fade);
    }

    static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // ------------------------------------------------------------------- build

    void Compose()
    {
        _canvas = new GameObject("Sticks");
        _canvas.transform.SetParent(transform, false);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // TankSticks' slot: under TouchControls (15) so MENU stays tappable.
        canvas.sortingOrder = 14;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _canvas.GetComponent<RectTransform>();

        _left = MakeStick("Steer", RingIdle, KnobIdle);
        _right = MakeStick("Throttle", RingAim, KnobAim);

        // The verb column, above the overlay strip on the right edge —
        // always faintly there, because a button that only appears sometimes
        // is a button nobody learns. Order: the pair you tap in a panic on
        // top, the pair you tap on purpose below.
        _column.Add(MakeButton("FLARES", new Vector2(1f, 0f), new Vector2(-96f, 560f), ButtonSize,
            () => FlaresTapped = true));
        _missile = MakeButton("FIRE", new Vector2(1f, 0f), new Vector2(-96f, 452f), ButtonSize,
            () => MissileTapped = true);
        _column.Add(_missile);
        _column.Add(MakeButton("MORPH", new Vector2(1f, 0f), new Vector2(-96f, 344f), ButtonSize,
            () => TransformTapped = true));
        _column.Add(MakeButton("CAM", new Vector2(1f, 0f), new Vector2(-96f, 236f), ButtonSize,
            () => CameraTapped = true));

        // The gunfight hand, shown only while the hero stands on the deck:
        // twin held FIRE buttons and JUMP, each at TouchControls' own spot,
        // so landing drops the thumbs onto the layout they already know.
        _fireRight = MakeButton("FIRE", new Vector2(1f, 0f), new Vector2(-370f, 545f), 180f,
            null, hold: true);
        _fireLeft = MakeButton("FIRE", new Vector2(0f, 0f), new Vector2(300f, 620f), 180f,
            null, hold: true);
        _jump = MakeButton("JUMP", new Vector2(1f, 0f), new Vector2(-150f, 150f), 170f,
            () => JumpTapped = true);
        SetButtonActive(_fireRight, false);
        SetButtonActive(_fireLeft, false);
        SetButtonActive(_jump, false);
    }

    Stick MakeStick(string name, Color ringColor, Color knobColor)
    {
        return new Stick
        {
            ringColor = ringColor,
            knobColor = knobColor,
            baseRect = MakeCircle($"{name}Base", RingSprite(), ringColor, Radius * 2f, true),
            knob = MakeCircle($"{name}Knob", DiscSprite(), knobColor, Radius * 0.72f, true),
        };
    }

    TapButton MakeButton(string label, Vector2 anchor, Vector2 cornerOffset, float size,
        System.Action tapped, bool hold = false)
    {
        var rect = MakeCircle($"Tap{label}", DiscSprite(), ButtonFace, size, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = cornerOffset;

        var text = new GameObject("Label").AddComponent<Text>();
        text.transform.SetParent(rect, false);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = Mathf.RoundToInt(size * 0.185f);
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(1f, 1f, 1f, 0.8f);
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

        var button = new TapButton
        {
            rect = rect,
            face = rect.GetComponent<Image>(),
            label = text,
            tapped = tapped,
            hold = hold,
            home = cornerOffset,
        };
        _buttons.Add(button);
        return button;
    }

    RectTransform MakeCircle(string name, Sprite sprite, Color color, float size, bool startHidden)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = startHidden ? Fade(color, 0f) : color;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        return rect;
    }

    static Sprite DiscSprite()
    {
        if (_disc == null) _disc = Circle("DogfightStickDisc", 0f);
        return _disc;
    }

    static Sprite RingSprite()
    {
        if (_ring == null) _ring = Circle("DogfightStickRing", 0.82f);
        return _ring;
    }

    static Sprite Circle(string name, float hollow)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                    new Vector2(radius, radius));
                float outer = Mathf.Clamp01(radius - d);
                float inner = hollow > 0f ? Mathf.Clamp01(d - radius * hollow) : 1f;
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)(255f * Mathf.Clamp01(Mathf.Min(outer, inner))));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
