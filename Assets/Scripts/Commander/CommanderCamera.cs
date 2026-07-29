using UnityEngine;

/// <summary>
/// The RTS camera: a fixed 55° look-down that pans across the battlefield and
/// zooms between whole-base and single-skirmish framing. No rotation — a fixed
/// yaw is what keeps "north is the enemy" true for a whole match, on screen
/// and in the player's head.
///
/// The camera state is a ground FOCUS point plus a height; the transform is
/// derived from those every frame. Storing the derived transform instead would
/// let pan and zoom fight each other.
/// </summary>
public class CommanderCamera : MonoBehaviour
{
    public const float Pitch = 55f;
    public const float Fov = 45f;

    const float MinHeight = 25f;
    const float MaxHeight = 80f;
    const float ZoomStep = 6f;

    /// <summary>Pan speed scales with height: zoomed out crosses the map fast.</summary>
    const float PanPerHeight = 1.1f;

    /// <summary>Screen-edge band that auto-pans, in pixels.</summary>
    const float EdgeMargin = 12f;

    float _height = 45f;
    Vector3 _focus;
    Vector3 _lastDragMouse;

    /// <summary>Jump the view so <paramref name="focus"/> is centre-screen.</summary>
    public void SnapTo(Vector3 focus)
    {
        _focus = focus;
        Apply();
    }

    void Update()
    {
        // Clamped: unscaled time is not capped by maximumDeltaTime, and a
        // backgrounded WebGL tab would otherwise bank a minute of edge-pan
        // and spend it all on its first frame back.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        // --- keyboard + screen-edge pan ---
        var pan = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        // Edge scroll is desktop-mouse-only, three ways:
        //  - not while touch drives input: the simulated mouse position parks
        //    wherever the last finger lifted (or at (0,0) before any touch),
        //    and a resting position inside the edge band pans forever;
        //  - not while middle-dragging: the band would fight the grab and
        //    slide the grabbed point off the cursor;
        //  - not while the cursor reports outside the window, which is what
        //    the editor does whenever the mouse rests on another panel.
        var mouse = Input.mousePosition;
        if (!TouchControls.Active && !Input.GetMouseButton(2)
            && mouse.x >= 0f && mouse.x <= Screen.width && mouse.y >= 0f && mouse.y <= Screen.height)
        {
            if (mouse.x < EdgeMargin) pan.x -= 1f;
            else if (mouse.x > Screen.width - EdgeMargin) pan.x += 1f;
            if (mouse.y < EdgeMargin) pan.y -= 1f;
            else if (mouse.y > Screen.height - EdgeMargin) pan.y += 1f;
        }

        pan = Vector2.ClampMagnitude(pan, 1f);
        _focus += new Vector3(pan.x, 0f, pan.y) * (PanPerHeight * _height * dt);

        // --- middle-drag: grab the ground and pull it ---
        if (Input.GetMouseButtonDown(2))
            _lastDragMouse = mouse;
        if (Input.GetMouseButton(2))
        {
            Vector3 delta = mouse - _lastDragMouse;
            _lastDragMouse = mouse;

            // World metres per screen pixel at the focus distance, so a point
            // grabbed under the cursor stays (approximately) under it.
            float dist = _height / Mathf.Sin(Pitch * Mathf.Deg2Rad);
            float perPixel = 2f * dist * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) / Screen.height;
            // Screen-vertical motion covers more ground than screen-horizontal
            // by the foreshortening of the pitched view.
            _focus -= new Vector3(delta.x * perPixel, 0f,
                                  delta.y * perPixel / Mathf.Sin(Pitch * Mathf.Deg2Rad));
        }

        // --- wheel zoom ---
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
            _height = Mathf.Clamp(_height - scroll * ZoomStep, MinHeight, MaxHeight);

        // Keep the focus on the map. The margin stops the view burying itself
        // in a border cliff at full pan. The southern limit is height-aware:
        // the camera stands `back` metres south of the focus, so a symmetric
        // clamp would walk the camera itself out past the cliff ring on a
        // full-south pan and fill the lower screen with void.
        float extent = CommanderMap.HalfExtent - 6f;
        float back = _height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
        _focus.x = Mathf.Clamp(_focus.x, -extent, extent);
        _focus.z = Mathf.Clamp(_focus.z, back - (CommanderMap.HalfExtent + 4f), extent);

        Apply();
    }

    void Apply()
    {
        // Stand back from the focus by however far a 55° look needs to hit it.
        float back = _height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
        transform.position = new Vector3(_focus.x, _height, _focus.z - back);
        transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
    }
}
