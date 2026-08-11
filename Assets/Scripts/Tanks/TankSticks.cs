using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tablet hands for TANK RAID: a stick under each thumb — left drives the
/// hull, right turns the turret — and no fire button, because the guns never
/// stop.
///
/// RAW TOUCHES, NOT uGUI. Two reasons, and either alone would be enough. The
/// first is that this mode needs two fingers held down at once and read
/// independently every frame, which the EventSystem's one-pointer-at-a-time
/// button model does not give. The second is that <see cref="TouchControls"/>
/// switches <c>Input.simulateMouseWithTouches</c> OFF whenever the on-screen
/// controls are up, precisely so that a drag cannot also read as a click — so on
/// the device this is built for, uGUI would receive nothing at all.
///
/// BOTH STICKS ARE DYNAMIC, BUT NEVER HIDDEN: each rests visible in its own
/// bottom corner — so the screen itself says there are two sticks and which
/// thumb owns which — and then re-centres wherever that thumb actually lands.
/// On a screen with no bezel to feel for, a stick that comes to the thumb
/// beats a thumb that has to go and find the stick; a stick that cannot be
/// seen at all teaches nobody it exists.
///
/// The mouse stands in for a finger, so the mode can be driven from the editor
/// with '=' held down — one pointer at a time, which is enough to check that a
/// stick claims, deflects and releases.
/// </summary>
public class TankSticks : MonoBehaviour
{
    /// <summary>Where to drive, world XZ, -1..1 per axis. Screen up is up the field.</summary>
    public static Vector2 Drive { get; private set; }

    /// <summary>Where to shoot, same shape. Zero while the thumb is off the stick.</summary>
    public static Vector2 Aim { get; private set; }

    /// <summary>True while the sticks own input — the mode falls back to keyboard otherwise.</summary>
    public static bool Showing { get; private set; }

    /// <summary>Stick travel in canvas units (reference height 1080) before full deflection.</summary>
    const float Radius = 145f;

    /// <summary>Travel that does nothing, so a resting thumb neither drives nor slews.</summary>
    const float DeadZone = 0.16f;

    /// <summary>Mouse pointers are given an id no finger can have.</summary>
    const int MouseId = -1971;

    static readonly Color RingIdle = new Color(0.2f, 0.9f, 1f, 0.30f);
    static readonly Color KnobIdle = new Color(0.2f, 0.9f, 1f, 0.55f);
    static readonly Color RingAim = new Color(1f, 0.72f, 0.25f, 0.30f);
    static readonly Color KnobAim = new Color(1f, 0.72f, 0.25f, 0.6f);

    class Stick
    {
        public RectTransform baseRect;
        public RectTransform knob;
        public Color ringColor;
        public Color knobColor;
        public int finger = int.MinValue;
        public Vector2 center;
        public Vector2 value;
        /// <summary>0 while the thumb is off, 1 while it is on; eased between.</summary>
        public float fade;
        public bool Held => finger != int.MinValue;
    }

    GameObject _canvas;
    RectTransform _canvasRect;
    Stick _left, _right;
    static Sprite _disc, _ring;

    readonly List<int> _seen = new List<int>();

    public static TankSticks Build(Transform parent)
    {
        var go = new GameObject("TankSticks");
        go.transform.SetParent(parent, false);
        return go.AddComponent<TankSticks>();
    }

    void OnDestroy()
    {
        Drive = Aim = Vector2.zero;
        Showing = false;
    }

    void Update()
    {
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
            Drive = Aim = Vector2.zero;
            return;
        }

        ReadPointers();
        // An unheld stick sits at its home corner. Re-asserted every frame
        // rather than only on release, so a resolution or orientation change
        // walks the resting sticks to where the corners now are.
        if (!_left.Held) Rest(_left, true);
        if (!_right.Held) Rest(_right, false);
        Drive = _left.value;
        Aim = _right.value;
        Paint(_left);
        Paint(_right);
    }

    /// <summary>The corner a stick waits in until its thumb lands.</summary>
    Vector2 Home(bool leftSide)
    {
        // A canvas built this frame has not been laid out yet; its rect says
        // zero. One frame at the canvas centre is invisible at these alphas.
        Vector2 half = _canvasRect.rect.size * 0.5f;
        return new Vector2((leftSide ? -1f : 1f) * (half.x - 300f), -(half.y - 250f));
    }

    void Rest(Stick stick, bool leftSide)
    {
        stick.center = Home(leftSide);
        stick.baseRect.anchoredPosition = stick.center;
        stick.knob.anchoredPosition = stick.center;
    }

    /// <summary>
    /// Claim, track and release. A finger belongs to whichever half of the
    /// screen it landed in and keeps that stick until it lifts — it may then
    /// wander anywhere, including across the middle, which is what stops a
    /// hard-over drive stick letting go the moment the thumb crosses the line.
    /// </summary>
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

        // The editor stand-in. Only consulted when there are no real touches, so
        // a device that reports both never gets two pointers for one thumb.
        if (Input.touchCount == 0 && Input.GetMouseButton(0))
        {
            Feed(MouseId, Input.mousePosition, Input.GetMouseButtonDown(0), false);
            _seen.Add(MouseId);
        }

        // Anything we still hold that stopped reporting has lifted. Touches do
        // not always deliver their Ended phase — an application pause or a
        // gesture the OS takes over both drop it — and a stick left holding a
        // dead finger drives the tank into the fence forever.
        if (_left.Held && !_seen.Contains(_left.finger)) Release(_left);
        if (_right.Held && !_seen.Contains(_right.finger)) Release(_right);
    }

    void Feed(int id, Vector2 screenPoint, bool began, bool ended)
    {
        var stick = _left.finger == id ? _left : _right.finger == id ? _right : null;

        if (stick == null)
        {
            if (!began)
                return;
            // The MENU button belongs to TouchControls and is hand-hit-tested
            // there, so the EventSystem cannot see it; ask before claiming, or a
            // tap on MENU also plants a stick under it.
            if (TouchControls.PointOver(screenPoint))
                return;
            stick = screenPoint.x < Screen.width * 0.5f ? _left : _right;
            if (stick.Held)
                return;                       // that thumb is already down
            stick.finger = id;
            stick.center = ToCanvas(screenPoint);
            stick.baseRect.anchoredPosition = stick.center;
        }

        if (ended)
        {
            Release(stick);
            return;
        }

        Vector2 offset = ToCanvas(screenPoint) - stick.center;
        Vector2 clamped = Vector2.ClampMagnitude(offset, Radius);
        stick.knob.anchoredPosition = stick.center + clamped;

        Vector2 raw = clamped / Radius;
        float magnitude = raw.magnitude;
        // Rescaled past the dead zone rather than simply gated, so the first
        // millimetre of real travel is a crawl instead of a jump to 16%.
        stick.value = magnitude <= DeadZone
            ? Vector2.zero
            : raw.normalized * ((magnitude - DeadZone) / (1f - DeadZone));
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

    /// <summary>Both rings brighten under the thumb and settle back when it
    /// lifts — to RESTING visibility, not to nothing: an invisible stick is a
    /// control the player has to discover by accident.</summary>
    void Paint(Stick stick)
    {
        stick.fade = Mathf.MoveTowards(stick.fade, stick.Held ? 1f : 0f, Time.deltaTime * 6f);
        float lift = Mathf.Lerp(0.55f, 1f, stick.fade);
        stick.baseRect.GetComponent<Image>().color = Fade(stick.ringColor, stick.ringColor.a * lift);
        stick.knob.GetComponent<Image>().color = Fade(stick.knobColor, stick.knobColor.a * lift);
    }

    static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

    // ------------------------------------------------------------------- build

    void Compose()
    {
        _canvas = new GameObject("Sticks");
        _canvas.transform.SetParent(transform, false);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Under TouchControls (15) so its MENU button stays on top and tappable.
        canvas.sortingOrder = 14;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _canvas.GetComponent<RectTransform>();

        _left = MakeStick("Drive", RingIdle, KnobIdle);
        _right = MakeStick("Aim", RingAim, KnobAim);
    }

    Stick MakeStick(string name, Color ringColor, Color knobColor)
    {
        return new Stick
        {
            ringColor = ringColor,
            knobColor = knobColor,
            baseRect = MakeCircle($"{name}Base", RingSprite(), ringColor, Radius * 2f),
            knob = MakeCircle($"{name}Knob", DiscSprite(), knobColor, Radius * 0.72f),
        };
    }

    RectTransform MakeCircle(string name, Sprite sprite, Color color, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Fade(color, 0f);
        image.raycastTarget = false;
        var rect = image.rectTransform;
        // Anchored at the canvas centre so anchoredPosition IS the canvas-space
        // point ScreenPointToLocalPointInRectangle hands back.
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        return rect;
    }

    // Explicit null tests rather than ??=, which uses C#'s null and not Unity's:
    // a sprite destroyed with the scene it was made in reads as "fake null" and
    // would be handed straight back.
    static Sprite DiscSprite()
    {
        if (_disc == null) _disc = Circle("TankStickDisc", 0f);
        return _disc;
    }

    static Sprite RingSprite()
    {
        if (_ring == null) _ring = Circle("TankStickRing", 0.82f);
        return _ring;
    }

    /// <summary>
    /// A soft circle, generated once. <paramref name="hollow"/> is where the
    /// ring's inner edge sits as a fraction of the radius; zero fills it.
    /// </summary>
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
                // One-pixel ramps at both edges: without them a circle this size
                // has visibly stepped sides on a phone screen.
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
