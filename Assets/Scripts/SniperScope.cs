using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sniper scope: narrows the first-person camera to pick out targets across
/// the arena, and draws the lens over the view while it's up.
///
/// Distinct from <see cref="XRayScope"/>, which reveals enemies THROUGH cover
/// but shows them at the same size — this one makes far targets big enough to
/// hit. They stack happily: x-ray tells you where a robot is, the scope lets
/// you shoot it.
///
/// A TOGGLE rather than a hold, on both the key and the button. Aiming on a
/// touch screen means dragging, and asking a thumb to hold a button down while
/// the other one drags leaves nothing to fire with.
///
/// Aim sensitivity scales with the zoom (<see cref="LookScale"/>): at 3.5x, a
/// swipe that used to sweep the room now nudges across a doorway, which is the
/// whole point of a scope.
/// </summary>
public class SniperScope : MonoBehaviour
{
    [Tooltip("Field of view while scoped. The camera's own FOV is the un-zoomed one.")]
    [Range(8f, 50f)] public float zoomedFov = 20f;

    [Tooltip("Seconds to zoom in or out.")]
    public float zoomSeconds = 0.16f;

    public Color reticleColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    /// <summary>Scoped right now (including mid-zoom)?</summary>
    public bool IsScoped { get; private set; }

    /// <summary>
    /// Look-sensitivity multiplier for the current zoom — 1 wide open, ~0.3 at
    /// full magnification. Follows the live FOV so it eases in with the zoom
    /// instead of snapping the moment the button is pressed.
    /// </summary>
    public float LookScale => _camera != null && _baseFov > 0f
        ? Mathf.Clamp(_camera.fieldOfView / _baseFov, 0.05f, 1f) : 1f;

    /// <summary>Magnification, for the readout.</summary>
    public float Magnification => _baseFov > 0f ? _baseFov / Mathf.Max(1f, zoomedFov) : 1f;

    Camera _camera;
    float _baseFov;
    GameObject _overlay;
    CanvasGroup _group;
    Text _readout;
    EnergyShield _shield;
    AimAssist _assist;
    RectTransform _lock;
    CanvasGroup _lockGroup;
    RectTransform _canvasRect;

    static Sprite _lensMask;

    void Awake()
    {
        _camera = GetComponentInChildren<Camera>(true);
        if (_camera != null)
            _baseFov = _camera.fieldOfView;
        // Not the assist: PlayerBrain adds this component and that one in the
        // same Awake, so at this point ours may be the one that exists yet.
        // Looked up on first use instead.

        // De-rezzing while scoped would come back zoomed with no way to tell why.
        _shield = GetComponent<EnergyShield>();
        if (_shield != null)
            _shield.OnDeRezzed += Unscope;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= Unscope;
    }

    /// <summary>The brain is switched off with the match — never stay zoomed into a menu.</summary>
    void OnDisable() => Unscope();

    public void Toggle() => SetScoped(!IsScoped);

    public void SetScoped(bool scoped)
    {
        if (scoped == IsScoped || _camera == null)
            return;
        IsScoped = scoped;
        if (scoped && _overlay == null)
            BuildOverlay();
        if (_overlay != null && scoped)
            _overlay.SetActive(true);
    }

    void Unscope() => SetScoped(false);

    void Update()
    {
        if (_camera == null || _baseFov <= 0f)
            return;

        float target = IsScoped ? zoomedFov : _baseFov;
        _camera.fieldOfView = Mathf.MoveTowards(_camera.fieldOfView, target,
            Mathf.Abs(_baseFov - zoomedFov) / Mathf.Max(0.01f, zoomSeconds) * Time.deltaTime);

        if (_overlay == null)
            return;

        // The lens fades with the zoom, so it never sits over a wide view.
        float zoomed = Mathf.InverseLerp(_baseFov, zoomedFov, _camera.fieldOfView);
        _group.alpha = zoomed;
        if (!IsScoped && zoomed <= 0.001f)
        {
            _overlay.SetActive(false);
            return;
        }
        if (_readout != null)
            _readout.text = $"{Magnification:0.#}x";
        UpdateLock();
    }

    /// <summary>
    /// Puts the bracket on whatever the aim assist has hold of.
    ///
    /// The bracket exists so the help is legible. An assist the player cannot
    /// see is one they cannot trust — a shot that lands feels like luck and a
    /// crosshair that slides feels like a bug. With the brackets on the robot
    /// it is reading, both become obviously deliberate, and the player learns
    /// the one thing they need to know: get it near, and it will take it.
    /// </summary>
    void UpdateLock()
    {
        if (_lock == null)
            return;
        if (_assist == null)
            _assist = GetComponent<AimAssist>();

        var target = _assist != null ? _assist.Target : null;
        if (target == null)
        {
            _lockGroup.alpha = Mathf.MoveTowards(_lockGroup.alpha, 0f, 6f * Time.deltaTime);
            return;
        }

        Vector3 screen = _camera.WorldToScreenPoint(_assist.TargetPoint);
        if (screen.z <= 0f)
        {
            _lockGroup.alpha = 0f;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, screen, null, out Vector2 local);
        _lock.anchoredPosition = local;

        // Brighter the more of the aim the assist is actually taking, so the
        // player can see the difference between "I could help with that one"
        // and "I am helping right now".
        _lockGroup.alpha = Mathf.MoveTowards(_lockGroup.alpha,
            0.45f + 0.55f * _assist.Engagement, 8f * Time.deltaTime);
    }

    void BuildOverlay()
    {
        _overlay = new GameObject("SniperOverlay");
        _overlay.transform.SetParent(transform, false);
        var canvas = _overlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the HUD (0) and the mode overlay (10), under the transformation
        // replay (12) and the on-screen controls (15) — the scope must never
        // swallow the buttons that turn it off.
        canvas.sortingOrder = 11;
        var scaler = _overlay.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _group = _overlay.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
        // The scaler works in reference units; the lock bracket is placed from
        // real screen pixels, so it needs the canvas rect to convert through.
        _canvasRect = _overlay.GetComponent<RectTransform>();

        // Darkened surround with the lens punched out of it. Stretched to the
        // screen, so the lens is an ellipse on a wide display — which is what a
        // scope filmed through a rectangular frame looks like anyway.
        var lens = MakeImage(_overlay.transform, "Lens", new Color(0.01f, 0.02f, 0.04f, 0.97f));
        lens.sprite = LensMask();
        lens.rectTransform.anchorMin = Vector2.zero;
        lens.rectTransform.anchorMax = Vector2.one;
        lens.rectTransform.offsetMin = Vector2.zero;
        lens.rectTransform.offsetMax = Vector2.zero;

        // Crosshair: four spokes with a gap in the middle, so the HUD's own
        // crosshair still reads inside it, plus range ticks down the vertical.
        Spoke(new Vector2(0f, 150f), new Vector2(2f, 220f));
        Spoke(new Vector2(0f, -150f), new Vector2(2f, 220f));
        Spoke(new Vector2(-150f, 0f), new Vector2(220f, 2f));
        Spoke(new Vector2(150f, 0f), new Vector2(220f, 2f));
        for (int i = 1; i <= 3; i++)
            Spoke(new Vector2(0f, -60f * i), new Vector2(20f - i * 3f, 2f));

        _readout = MakeText(_overlay.transform, "Zoom", "", 22,
            new Vector2(0f, -300f), new Vector2(200f, 34f));
        MakeText(_overlay.transform, "Label", "SNIPER  SCOPE", 20,
            new Vector2(0f, 300f), new Vector2(400f, 30f));

        BuildLock();
    }

    /// <summary>
    /// Four corner ticks around the locked robot. Corners rather than a box:
    /// a closed rectangle over a distant robot hides the robot, and the whole
    /// point of the scope is seeing it.
    ///
    /// Built last so it draws over the crosshair — the bracket and the spokes
    /// meet at the same place the moment the assist has finished its work, and
    /// the one that should read on top is the one that says "on target".
    /// </summary>
    void BuildLock()
    {
        var go = new GameObject("Lock");
        go.transform.SetParent(_overlay.transform, false);
        _lock = go.AddComponent<RectTransform>();
        _lock.anchorMin = _lock.anchorMax = new Vector2(0.5f, 0.5f);
        _lock.sizeDelta = Vector2.zero;
        _lockGroup = go.AddComponent<CanvasGroup>();
        _lockGroup.alpha = 0f;
        _lockGroup.blocksRaycasts = false;

        const float reach = 46f, thickness = 3f, arm = 18f;
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = (corner & 1) == 0 ? -1f : 1f;
            float sy = (corner & 2) == 0 ? -1f : 1f;
            Tick(new Vector2(sx * (reach - arm * 0.5f), sy * reach), new Vector2(arm, thickness));
            Tick(new Vector2(sx * reach, sy * (reach - arm * 0.5f)), new Vector2(thickness, arm));
        }
    }

    void Tick(Vector2 position, Vector2 size)
    {
        var image = MakeImage(_lock, "Tick", reticleColor);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        image.rectTransform.anchoredPosition = position;
        image.rectTransform.sizeDelta = size;
    }

    void Spoke(Vector2 position, Vector2 size)
    {
        var image = MakeImage(_overlay.transform, "Spoke", reticleColor);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        image.rectTransform.anchoredPosition = position;
        image.rectTransform.sizeDelta = size;
    }

    Text MakeText(Transform parent, string name, string content, int size,
        Vector2 position, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(reticleColor.r, reticleColor.g, reticleColor.b, 0.6f);
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        return text;
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// Opaque everywhere except a soft-edged hole in the middle. Generated
    /// rather than imported for the same reason the touch controls generate
    /// their circles: the project ships no sprite atlas.
    /// </summary>
    static Sprite LensMask()
    {
        if (_lensMask != null)
            return _lensMask;

        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "SniperLens",
            hideFlags = HideFlags.HideAndDontSave,
        };

        var centre = new Vector2(size * 0.5f, size * 0.5f);
        float hole = size * 0.44f;
        float feather = size * 0.05f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                float alpha = Mathf.Clamp01((distance - hole) / feather);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        _lensMask = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        _lensMask.hideFlags = HideFlags.HideAndDontSave;
        return _lensMask;
    }
}
