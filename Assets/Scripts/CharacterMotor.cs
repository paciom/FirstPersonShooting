using UnityEngine;

/// <summary>
/// Physical movement layer shared by every character. Both PlayerBrain and
/// AIBrain drive this same interface, which keeps Player-vs-AI and AI-vs-AI
/// perfectly symmetric.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 9f;
    public float jumpHeight = 1.2f;
    public float gravity = -22f;

    [Header("Look")]
    public Transform head;           // pitched up/down; body handles yaw
    public float minPitch = -85f;
    public float maxPitch = 85f;

    CharacterController _controller;
    Vector2 _moveInput;
    bool _sprinting;
    float _verticalVelocity;
    float _pitch;

    public bool IsGrounded => _controller.isGrounded;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public void SetMoveInput(Vector2 input) => _moveInput = Vector2.ClampMagnitude(input, 1f);

    public void SetSprint(bool sprinting) => _sprinting = sprinting;

    public void AddLook(Vector2 delta)
    {
        transform.Rotate(0f, delta.x, 0f);
        _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        if (head != null)
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    public void Jump()
    {
        if (_controller.isGrounded)
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    public void Teleport(Vector3 position)
    {
        _controller.enabled = false;
        transform.position = position;
        _verticalVelocity = 0f;
        _controller.enabled = true;
    }

    void Update()
    {
        float speed = _sprinting ? sprintSpeed : walkSpeed;
        Vector3 planar = transform.TransformDirection(new Vector3(_moveInput.x, 0f, _moveInput.y)) * speed;

        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = planar + Vector3.up * _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }
}
