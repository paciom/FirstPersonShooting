using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The tablet's hands, reading raw touches — TouchControls deliberately
/// switches OFF mouse simulation when it activates, so the mouse-based
/// selection and camera never hear a finger. This fills that hole:
///
///   one finger drag   — pan the camera (grab the ground and pull)
///   two finger pinch  — zoom
///   tap on own robot  — select it
///   tap elsewhere     — order the selection there (enemy = attack), or
///                       clear the selection when nothing is selected
///
/// Box select, control groups and attack-move stay keyboard/mouse-only;
/// the minimap (plain uGUI, touch-native) covers long-range navigation.
/// Idle whenever the touch scheme is inactive, so a desktop mouse never
/// fights two input paths.
/// </summary>
public class CommanderTouch : MonoBehaviour
{
    const float TapSlop = 18f;       // px — fingers wobble more than mice
    const float TapSeconds = 0.35f;

    CommanderCamera _camera;
    CommanderSelection _selection;   // null in spectate: taps then only pan

    int _fingerId = -1;
    Vector2 _downAt;
    float _downTime;
    bool _panning;
    float _lastPinchDistance;

    public void Bind(CommanderCamera camera, CommanderSelection selection)
    {
        _camera = camera;
        _selection = selection;
    }

    void Update()
    {
        if (!TouchControls.Active || _camera == null)
            return;

        // While a build ghost is up, the finger belongs to the placer's own
        // touch path — same yield the mouse selection makes. Either placer:
        // this component serves both strategy modes.
        if (BuildPlacer.Active || TDPlacer.Active)
        {
            _fingerId = -1;
            return;
        }

        if (Input.touchCount >= 2)
        {
            TickPinch();
            _fingerId = -1;   // a second finger cancels any tap-in-progress
            return;
        }
        _lastPinchDistance = 0f;

        if (Input.touchCount == 1)
            TickFinger(Input.GetTouch(0));
    }

    void TickFinger(Touch touch)
    {
        switch (touch.phase)
        {
            case TouchPhase.Began:
                // Fingers on UI (MENU, build bar, minimap) belong to the UI.
                if (TouchControls.PointOver(touch.position)
                    || (EventSystem.current != null
                        && EventSystem.current.IsPointerOverGameObject(touch.fingerId)))
                {
                    _fingerId = -1;
                    return;
                }
                _fingerId = touch.fingerId;
                _downAt = touch.position;
                _downTime = Time.unscaledTime;
                _panning = false;
                break;

            case TouchPhase.Moved:
                if (touch.fingerId != _fingerId)
                    return;
                if (!_panning && (touch.position - _downAt).magnitude > TapSlop)
                    _panning = true;
                if (_panning)
                    _camera.PanBy(touch.deltaPosition);
                break;

            case TouchPhase.Ended:
                if (touch.fingerId != _fingerId)
                    return;
                _fingerId = -1;
                if (_panning || Time.unscaledTime - _downTime > TapSeconds)
                    return;
                Tap(touch.position);
                break;

            case TouchPhase.Canceled:
                if (touch.fingerId == _fingerId)
                    _fingerId = -1;
                break;
        }
    }

    void Tap(Vector2 position)
    {
        // Null in spectate — taps then only ever panned, which is correct.
        _selection?.Tap(position);
    }

    void TickPinch()
    {
        var a = Input.GetTouch(0);
        var b = Input.GetTouch(1);
        float distance = (a.position - b.position).magnitude;
        if (_lastPinchDistance > 0f)
            _camera.ZoomBy(distance - _lastPinchDistance);
        _lastPinchDistance = distance;
    }
}
