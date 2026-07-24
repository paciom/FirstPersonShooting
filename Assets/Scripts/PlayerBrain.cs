using UnityEngine;

/// <summary>
/// Human input → CharacterMotor + LaserBlaster. Uses the legacy Input API for
/// the greybox milestone (active input handling is set to "Both"); migrating to
/// the Input System package is planned for the polish phase.
/// </summary>
[RequireComponent(typeof(CharacterMotor))]
public class PlayerBrain : MonoBehaviour
{
    public static PlayerBrain Local { get; private set; }

    public float mouseSensitivity = 2.2f;
    public LaserBlaster blaster;

    CharacterMotor _motor;

    void Awake()
    {
        Local = this;
        _motor = GetComponent<CharacterMotor>();
        if (blaster == null)
            blaster = GetComponentInChildren<LaserBlaster>();
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // Cursor locking and the Escape key are owned by GameModeController.

    void Update()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            _motor.AddLook(new Vector2(
                Input.GetAxis("Mouse X") * mouseSensitivity,
                Input.GetAxis("Mouse Y") * mouseSensitivity));
        }

        _motor.SetMoveInput(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")));
        _motor.SetSprint(Input.GetKey(KeyCode.LeftShift));

        if (Input.GetKeyDown(KeyCode.Space))
            _motor.Jump();

        if (blaster != null && Input.GetMouseButton(0) && Cursor.lockState == CursorLockMode.Locked)
        {
            Transform aim = _motor.head != null ? _motor.head : transform;
            blaster.TryFire(aim.forward);
        }
    }
}
