using UnityEngine;

/// <summary>
/// Free-fly camera for Arena Builder mode: WASD to move, mouse to look,
/// Q/E down/up, Shift for speed. On a touch screen the same rig flies from
/// <see cref="TouchControls"/> — stick, look-drag, and UP/DOWN buttons.
/// </summary>
public class FlyCam : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float sprintMultiplier = 2.2f;
    public float mouseSensitivity = 2.2f;

    float _yaw;
    float _pitch;

    void Start()
    {
        var euler = transform.rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    void Update()
    {
        TouchControls touch = TouchControls.Active ? TouchControls.Instance : null;

        if (touch != null)
        {
            _yaw += touch.LookDelta.x;
            _pitch = Mathf.Clamp(_pitch - touch.LookDelta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        var planar = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        float lift = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        bool sprint = Input.GetKey(KeyCode.LeftShift);
        if (touch != null)
        {
            if (planar.sqrMagnitude < 0.01f)
            {
                planar = touch.Move;
                sprint = touch.Sprint;
            }
            lift += (touch.Rise ? 1f : 0f) - (touch.Sink ? 1f : 0f);
        }

        float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);
        Vector3 move = transform.right * planar.x
                     + transform.forward * planar.y
                     + Vector3.up * lift;
        transform.position += move * (speed * Time.deltaTime);
    }
}
