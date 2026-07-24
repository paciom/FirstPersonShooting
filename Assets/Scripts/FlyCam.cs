using UnityEngine;

/// <summary>
/// Free-fly camera for Arena Builder mode: WASD to move, mouse to look,
/// Q/E down/up, Shift for speed.
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
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);
        Vector3 move = transform.right * Input.GetAxisRaw("Horizontal")
                     + transform.forward * Input.GetAxisRaw("Vertical")
                     + Vector3.up * ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f));
        transform.position += move * (speed * Time.deltaTime);
    }
}
