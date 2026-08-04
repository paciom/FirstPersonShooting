using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The episode's cinematographer: one camera, placed shot by shot. Dialogue
/// cuts, tracking shots glide — the grammar every filmed conversation uses.
///
/// Hand-rolled like every other rig in this project (Spectator, Dogfight,
/// Quest, Show): a shot is a position, a gaze and a lens, nothing more. The
/// gaze is damped so a subject that gestures or rises keeps the frame without
/// the camera snapping; a follow shot re-derives its position from the actor
/// every frame through the same damping.
/// </summary>
public class StoryCamera : MonoBehaviour
{
    Camera _cam;

    Vector3 _shotPosition;        // world, or offset origin for follow shots
    Transform _follow;            // when set, position = follow + offset
    Vector3 _followOffset;
    Transform _gazeTransform;     // when set, aim here…
    Vector3 _gazePoint;           // …else here
    float _targetFov = 50f;

    Vector3 _aim;                 // damped gaze point
    float _blend;                 // seconds left in a glide, 0 = arrived
    float _blendTotal;
    Vector3 _fromPos;

    bool _hasShot;

    public static StoryCamera Build(Transform parent)
    {
        var rig = new GameObject("StoryCamera");
        rig.transform.SetParent(parent, false);
        rig.tag = "MainCamera";
        var cam = rig.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BrawlStage.VoidColor;
        cam.fieldOfView = 50f;
        rig.AddComponent<AudioListener>();
        var data = rig.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        var story = rig.AddComponent<StoryCamera>();
        story._cam = cam;
        return story;
    }

    /// <summary>
    /// Take a shot. blendSeconds 0 is a hard cut; anything else glides the
    /// camera from where it is — cuts for coverage, glides for reveals.
    /// </summary>
    public void SetShot(Vector3 position, Transform gaze, Vector3 gazeFallback,
        float fov, float blendSeconds = 0f, Transform follow = null, Vector3 followOffset = default)
    {
        _follow = follow;
        _followOffset = followOffset;
        _shotPosition = position;
        _gazeTransform = gaze;
        _gazePoint = gazeFallback;
        _targetFov = fov;

        Vector3 gazeNow = gaze != null ? gaze.position : gazeFallback;
        if (!_hasShot || blendSeconds <= 0f)
        {
            // The cut: everything lands this frame, including the damped aim —
            // a cut that then swings its look direction reads as a mistake.
            transform.position = DesiredPosition();
            _aim = gazeNow;
            transform.rotation = LookRotation(_aim);
            _cam.fieldOfView = fov;
            _blend = 0f;
        }
        else
        {
            _fromPos = transform.position;
            _blend = _blendTotal = blendSeconds;
            _aim = _aim == Vector3.zero ? gazeNow : _aim;
        }
        _hasShot = true;
    }

    Vector3 DesiredPosition()
    {
        return _follow != null ? _follow.position + _followOffset : _shotPosition;
    }

    void LateUpdate()
    {
        if (!_hasShot)
            return;

        Vector3 gazeNow = _gazeTransform != null ? _gazeTransform.position : _gazePoint;
        _aim = Vector3.Lerp(_aim, gazeNow, 1f - Mathf.Exp(-7f * Time.deltaTime));

        Vector3 desired = DesiredPosition();
        if (_blend > 0f)
        {
            _blend = Mathf.Max(0f, _blend - Time.deltaTime);
            float p = 1f - _blend / _blendTotal;
            float ease = p * p * (3f - 2f * p);
            transform.position = Vector3.Lerp(_fromPos, desired, ease);
        }
        else if (_follow != null)
        {
            transform.position = Vector3.Lerp(transform.position, desired,
                1f - Mathf.Exp(-5f * Time.deltaTime));
        }
        else
        {
            transform.position = desired;
        }

        transform.rotation = LookRotation(_aim);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _targetFov,
            1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    Quaternion LookRotation(Vector3 at)
    {
        Vector3 dir = at - transform.position;
        return dir.sqrMagnitude < 0.0001f ? transform.rotation
            : Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
