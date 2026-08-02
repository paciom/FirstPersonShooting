using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The front-end's shared look: procedurally generated panel sprites plus the
/// three small behaviours that give the main menu its motion.
///
/// Everything here is generated at runtime rather than authored as texture
/// assets, because these are all shapes a formula describes exactly — rounded
/// rectangles, linear fades, a radial vignette. Generating them keeps the
/// corner radius and the falloff curve readable in one place instead of
/// scattered across a dozen import settings, and costs a few hundred
/// microseconds once per session.
/// </summary>
public static class MenuArt
{
    static readonly Dictionary<float, Sprite> _rounded = new Dictionary<float, Sprite>();
    static readonly Dictionary<float, Sprite> _glow = new Dictionary<float, Sprite>();
    static Sprite _fadeDown, _fadeUp, _vignette;

    /// <summary>
    /// A rounded-rect panel sprite with anti-aliased corners, 9-sliced so the
    /// radius holds at any rect size. <paramref name="radius"/> is in the UI
    /// pixels the corners will actually occupy.
    /// </summary>
    public static Sprite RoundedRect(float radius)
    {
        if (_rounded.TryGetValue(radius, out var cached) && cached != null)
            return cached;

        // The border must clear the radius or the 9-slice would stretch part
        // of the curve across the middle and flatten it.
        int border = Mathf.CeilToInt(radius) + 8;
        int size = border * 2 + 2;
        var tex = NewTexture(size, $"MenuRound{radius}");
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance to the inset core box is a rounded rect's signed
                // distance field; a one-pixel ramp at the edge anti-aliases it.
                float d = CoreDistance(x, y, size, radius);
                byte a = (byte)(255f * Mathf.Clamp01(radius - d + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        return _rounded[radius] = Finish(tex, pixels, border);
    }

    /// <summary>
    /// A soft halo for the same rounded shape: alpha holds at full across the
    /// middle and falls away over <paramref name="radius"/> pixels, so drawing
    /// it on a rect inflated by that much wraps a card in an even glow.
    /// </summary>
    public static Sprite GlowRect(float radius)
    {
        if (_glow.TryGetValue(radius, out var cached) && cached != null)
            return cached;

        int border = Mathf.CeilToInt(radius) + 8;
        int size = border * 2 + 2;
        var tex = NewTexture(size, $"MenuGlow{radius}");
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = CoreDistance(x, y, size, radius);
                // Squared falloff reads as light rather than as a flat skirt.
                float f = Mathf.Clamp01(1f - d / radius);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * f * f));
            }
        }
        return _glow[radius] = Finish(tex, pixels, border);
    }

    /// <summary>Opaque at the top edge, clear at the bottom.</summary>
    public static Sprite FadeDown() => _fadeDown != null ? _fadeDown : _fadeDown = Fade(true);

    /// <summary>Clear at the top edge, opaque at the bottom.</summary>
    public static Sprite FadeUp() => _fadeUp != null ? _fadeUp : _fadeUp = Fade(false);

    /// <summary>Clear in the middle, opaque at the corners.</summary>
    public static Sprite Vignette()
    {
        if (_vignette != null)
            return _vignette;

        const int size = 128;
        var tex = NewTexture(size, "MenuVignette");
        var pixels = new Color32[size * size];
        var mid = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), mid) / (size * 0.5f);
                // Wide clear centre, late falloff. A tighter ellipse leaves its
                // most-transparent ring lying along the screen's midline, which
                // reads as a bright horizontal stripe across the whole width
                // rather than as darkened corners.
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1.5f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        }
        // No border: a vignette is stretched whole, never sliced.
        return _vignette = Finish(tex, pixels, 0);
    }

    static Sprite Fade(bool opaqueAtTop)
    {
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false)
        {
            name = opaqueAtTop ? "MenuFadeDown" : "MenuFadeUp",
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = y / (h - 1f);                       // 0 at the bottom row
            if (opaqueAtTop) t = 1f - t;
            // Smoothstep, not linear: a linear scrim shows a visible seam
            // where it meets the art.
            byte a = (byte)(255f * Mathf.SmoothStep(1f, 0f, t));
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
    }

    /// <summary>Distance from a pixel to the box inset by <paramref name="radius"/>.</summary>
    static float CoreDistance(int x, int y, int size, float radius)
    {
        float px = x + 0.5f, py = y + 0.5f;
        float cx = Mathf.Clamp(px, radius, size - radius);
        float cy = Mathf.Clamp(py, radius, size - radius);
        return Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
    }

    static Texture2D NewTexture(int size, string name) =>
        new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
        };

    static Sprite Finish(Texture2D tex, Color32[] pixels, int border)
    {
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        int size = tex.width;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }
}

/// <summary>
/// Tints a Graphic's mesh from <see cref="top"/> to <see cref="bottom"/> down
/// its own vertex bounds. On a Text that means the gradient spans the glyphs
/// rather than the layout rect, so a title reads as one piece of metal no
/// matter how much empty space its RectTransform carries.
///
/// Add this BEFORE Outline/Shadow: mesh modifiers run in component order, and
/// the outline copies must be duplicated from already-tinted vertices so they
/// stay a flat keyline instead of inheriting the ramp.
/// </summary>
public class UIGradient : BaseMeshEffect
{
    public Color top = Color.white;
    public Color bottom = Color.white;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
            return;

        var vertex = new UIVertex();
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            min = Mathf.Min(min, vertex.position.y);
            max = Mathf.Max(max, vertex.position.y);
        }

        float height = Mathf.Max(0.0001f, max - min);
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            var ramp = Color.Lerp(bottom, top, (vertex.position.y - min) / height);
            vertex.color = (Color)vertex.color * ramp;
            vh.SetUIVertex(vertex, i);
        }
    }
}

/// <summary>
/// The hover/press feel of one mode card: it lifts, its art brightens, its
/// accent bar lights up and a coloured halo blooms behind it.
///
/// This lives on the same object as the Button on purpose. Pointer down/up are
/// dispatched with ExecuteHierarchy, which stops at the first object that
/// handles them — on a parent it would never see a press, because the Button
/// underneath would swallow it first.
/// </summary>
public class MenuCard : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform lift;      // the wrapper that scales — glow included
    public Graphic art;
    public Graphic accent;
    public Graphic glow;
    public Text label;
    public Color accentIdle = Color.white;
    public Color accentHot = Color.white;

    float _blend;                   // 0 resting, 1 hovered
    bool _hot, _pressed;

    void Update()
    {
        // Unscaled: a menu that opens over a paused mode must still animate.
        _blend = Mathf.MoveTowards(_blend, _hot ? 1f : 0f, Time.unscaledDeltaTime * 7f);

        float scale = Mathf.Lerp(1f, 1.055f, _blend) * (_pressed ? 0.975f : 1f);
        if (lift != null)
            lift.localScale = new Vector3(scale, scale, 1f);

        if (art != null)
        {
            float v = Mathf.Lerp(0.82f, 1f, _blend) * (_pressed ? 0.8f : 1f);
            art.color = new Color(v, v, v, 1f);
        }
        if (accent != null)
            accent.color = Color.Lerp(accentIdle, accentHot, _blend);
        if (glow != null)
        {
            var c = accentHot;
            glow.color = new Color(c.r, c.g, c.b, _blend * 0.5f);
        }
        if (label != null)
            label.color = Color.Lerp(new Color(1f, 1f, 1f, 0.82f), Color.white, _blend);
    }

    public void OnPointerEnter(PointerEventData _)
    {
        _hot = true;
        // A lifted card has to outrank its neighbours or the next one along
        // would overlap the corner it just grew into.
        if (lift != null)
            lift.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData _) { _hot = false; _pressed = false; }
    public void OnPointerDown(PointerEventData _) => _pressed = true;
    public void OnPointerUp(PointerEventData _) => _pressed = false;
}

/// <summary>
/// A slow zoom-and-drift on the key art so the title screen breathes instead
/// of sitting there as a flat JPEG.
///
/// <see cref="baseScale"/> is above 1 deliberately: the art is sized to
/// envelope the screen exactly, so any drift at 1.0 would swing a bare edge
/// into view. The overscan is the drift budget.
/// </summary>
public class MenuKenBurns : MonoBehaviour
{
    public float period = 44f;
    public float baseScale = 1.06f;
    public float zoom = 0.05f;
    public float drift = 22f;

    RectTransform _rect;
    float _time;

    void Awake() => _rect = (RectTransform)transform;

    void Update()
    {
        _time += Time.unscaledDeltaTime;
        float a = _time / period * Mathf.PI * 2f;

        float scale = baseScale + zoom * (0.5f + 0.5f * Mathf.Sin(a));
        _rect.localScale = new Vector3(scale, scale, 1f);
        // Incommensurate rates on the two axes: the path never repeats tightly
        // enough for the eye to catch the loop.
        _rect.anchoredPosition = new Vector2(
            Mathf.Sin(a * 0.5f) * drift,
            Mathf.Sin(a * 0.37f) * drift * 0.45f);
    }
}
