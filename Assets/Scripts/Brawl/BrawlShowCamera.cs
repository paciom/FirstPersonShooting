using UnityEngine;

/// <summary>
/// The Martial Arts Show's viewing camera — an orbit the AUDIENCE drives.
///
/// The fight camera (<see cref="BrawlCamera"/>) is a director: it scores
/// angles for line-of-sight and cuts between them on its own beat. That is
/// right for a bout nobody is steering, and wrong here — every cut would
/// yank the view out from under someone trying to look at a move. So the
/// show gets its own rig instead of a flag on the shared one, which also
/// keeps the fight, Commander and Tower Defense cameras untouched.
///
/// Drag with the left mouse button (or one finger) to swing around the
/// performer; scroll (or pinch) to close in. Until the first drag the
/// camera orbits slowly by itself so the show still presents itself to a
/// passive viewer; after that it holds exactly where it was left and never
/// moves on its own again — a camera that drifts back is a camera that
/// argues with the person holding it.
/// </summary>
public class BrawlShowCamera : MonoBehaviour
{
    public Transform subject;

    /// <summary>Height above the performer's feet that the view pivots around.</summary>
    public float focusHeight = 1.0f;

    const float MinDistance = 3.5f;
    const float MaxDistance = 18f;
    const float MinPitch = -5f;
    const float MaxPitch = 78f;
    const float FloorClearance = 0.35f;

    const float DegreesPerPixel = 0.32f;
    const float ZoomPerScrollNotch = 1.4f;
    const float PinchZoomScale = 0.02f;
    const float IdleOrbitDegreesPerSecond = 8f;

    // Facing 205 degrees, so 25 puts the lens in front of the performer.
    float _yaw = 25f;
    float _pitch = 14f;
    float _distance = 7.5f;

    bool _audienceTookOver;
    bool _dragging;
    Vector3 _lastPointer;
    float _lastPinch;
    Vector3 _focus;
    bool _focusPrimed;

    /// <summary>True once the viewer has moved the camera themselves.</summary>
    public bool AudienceTookOver => _audienceTookOver;

    public void Frame(Transform performer)
    {
        subject = performer;
        _focusPrimed = false;
        // Place it now rather than waiting for the first LateUpdate, or the
        // opening frame is shot from the world origin.
        Place();
    }

    void LateUpdate()
    {
        if (subject == null)
            return;

        ReadPointer();
        ReadTouch();

        if (!_audienceTookOver)
            _yaw += IdleOrbitDegreesPerSecond * Time.deltaTime;

        Place();
    }

    void Place()
    {
        if (subject == null)
            return;

        Vector3 wanted = subject.position + Vector3.up * focusHeight;
        if (!_focusPrimed)
        {
            _focus = wanted;
            _focusPrimed = true;
        }
        else
        {
            // Follow the performer, but softly: the knockdown act slides the
            // body several metres and a rigid lock would whip the frame.
            _focus = Vector3.Lerp(_focus, wanted, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);

        Vector3 offset = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * _distance;
        Vector3 position = _focus + offset;

        // Swing far enough round and the backdrop wall or a corner pylon ends
        // up between the lens and the performer — a black screen the viewer
        // reads as a crash. Pull in to this side of whatever is in the way.
        // The performer's own capsule sits on Ignore Raycast, so it is never
        // the thing we pull in against.
        Vector3 direction = position - _focus;
        float reach = direction.magnitude;
        if (reach > 0.01f && Physics.SphereCast(_focus, 0.3f, direction / reach,
                out var blocker, reach, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            position = _focus + direction / reach * Mathf.Max(1.2f, blocker.distance - 0.25f);

        // Never dip under the deck — below the floor the performer is hidden
        // by the very stage they are standing on.
        position.y = Mathf.Max(position.y, FloorClearance);

        transform.position = position;
        transform.rotation = Quaternion.LookRotation(_focus - position, Vector3.up);
    }

    void ReadPointer()
    {
        if (Input.touchCount > 0)
            return;      // touch owns the gesture; don't double-count it

        if (Input.GetMouseButtonDown(0))
        {
            _dragging = true;
            _lastPointer = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(0))
            _dragging = false;

        if (_dragging && Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - _lastPointer;
            _lastPointer = Input.mousePosition;
            Swing(delta.x, delta.y);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
            Zoom(-scroll * ZoomPerScrollNotch);
    }

    void ReadTouch()
    {
        if (Input.touchCount == 1)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Moved)
                Swing(touch.deltaPosition.x, touch.deltaPosition.y);
            _lastPinch = 0f;
            return;
        }
        if (Input.touchCount >= 2)
        {
            float gap = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
            if (_lastPinch > 0f)
                Zoom((_lastPinch - gap) * PinchZoomScale);
            _lastPinch = gap;
            return;
        }
        _lastPinch = 0f;
    }

    void Swing(float dx, float dy)
    {
        if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f)
            return;
        _audienceTookOver = true;
        _yaw += dx * DegreesPerPixel;
        // Drag up to look down from above, which is the way round that reads
        // as moving the CAMERA rather than the world.
        _pitch = Mathf.Clamp(_pitch + dy * DegreesPerPixel, MinPitch, MaxPitch);
    }

    void Zoom(float amount)
    {
        _audienceTookOver = true;
        _distance = Mathf.Clamp(_distance + amount, MinDistance, MaxDistance);
    }
}
