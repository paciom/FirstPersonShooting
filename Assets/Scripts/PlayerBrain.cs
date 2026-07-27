using UnityEngine;

/// <summary>
/// Human input → CharacterMotor + Weapon. LMB fires the held weapon, RMB
/// engages the X-Ray scope. Uses the legacy Input API for now (active input
/// handling is "Both"); Input System migration is planned for the polish phase.
///
/// On a touch screen <see cref="TouchControls"/> takes over completely — see
/// the note in Update for why the two schemes are exclusive rather than merged.
///
/// The usable weapon list comes from <see cref="WeaponLoadout"/>: two basics
/// always, plus a third slot whenever an airdropped Weapon Pod has been picked
/// up. Number keys select a slot directly; scroll and Q/E cycle.
/// </summary>
[RequireComponent(typeof(CharacterMotor))]
public class PlayerBrain : MonoBehaviour
{
    public static PlayerBrain Local { get; private set; }

    public float mouseSensitivity = 2.2f;
    public Weapon[] weapons;
    public XRayScope scope;

    CharacterMotor _motor;
    WeaponLoadout _loadout;
    TransformMode _vehicle;
    int _loadoutVersion = -1;
    int _activeWeapon;

    public string CurrentWeaponName =>
        (weapons != null && weapons.Length > 0 && weapons[_activeWeapon] != null)
            ? weapons[_activeWeapon].weaponName : "";

    /// <summary>"2/3" style slot readout for the HUD.</summary>
    public string WeaponSlotLabel =>
        (weapons != null && weapons.Length > 0) ? $"{_activeWeapon + 1}/{weapons.Length}" : "";

    /// <summary>Signature color of the held weapon (tints the HUD label).</summary>
    public Color CurrentWeaponColor =>
        (weapons != null && weapons.Length > 0 && weapons[_activeWeapon] != null)
            ? weapons[_activeWeapon].color : Color.white;

    /// <summary>Seconds left on the airdropped weapon, or 0 when holding a basic.</summary>
    public float TreasureSecondsLeft =>
        (_loadout != null && _loadout.Special != null && _activeWeapon == _loadout.SpecialSlot)
            ? _loadout.SpecialSecondsLeft : 0f;

    void Awake()
    {
        Local = this;
        _motor = GetComponent<CharacterMotor>();
        _loadout = GetComponent<WeaponLoadout>();
        _vehicle = GetComponent<TransformMode>();
        if (weapons == null || weapons.Length == 0)
            weapons = GetComponentsInChildren<Weapon>();
        if (scope == null)
            scope = GetComponent<XRayScope>();
        RefreshLoadout();
        SetActiveWeapon(0);
    }

    /// <summary>
    /// Pull the usable set from the loadout when it changes (pod picked up, or
    /// the timer ran out). Keeps hold of the same weapon across the change when
    /// it's still usable, so an expiring pod doesn't shuffle the basics around.
    /// </summary>
    void RefreshLoadout()
    {
        if (_loadout == null || _loadout.Version == _loadoutVersion)
            return;
        _loadoutVersion = _loadout.Version;

        Weapon held = (weapons != null && weapons.Length > 0) ? weapons[_activeWeapon] : null;
        weapons = _loadout.Available;
        int slot = _loadout.SlotOf(held);
        SetActiveWeapon(slot >= 0 ? slot : Mathf.Min(_activeWeapon, weapons.Length - 1));
    }

    void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // Cursor locking and the Escape key are owned by GameModeController.

    void Update()
    {
        RefreshLoadout();

        // On a touch screen the on-screen controls are the only input: the
        // cursor is never locked there, and mouse buttons are suppressed so a
        // look-drag can't double as a trigger pull.
        TouchControls touch = TouchControls.Active ? TouchControls.Instance : null;
        bool armed = touch != null || Cursor.lockState == CursorLockMode.Locked;

        if (touch != null)
        {
            _motor.AddLook(touch.LookDelta);
        }
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            _motor.AddLook(new Vector2(
                Input.GetAxis("Mouse X") * mouseSensitivity,
                Input.GetAxis("Mouse Y") * mouseSensitivity));
        }

        var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool sprint = Input.GetKey(KeyCode.LeftShift);
        if (touch != null && move.sqrMagnitude < 0.01f)
        {
            move = touch.Move;
            sprint = touch.Sprint;
        }
        _motor.SetMoveInput(move);
        _motor.SetSprint(sprint);

        if (Input.GetKeyDown(KeyCode.Space) || (touch != null && touch.ConsumeJump()))
            _motor.Jump();

        // V folds into vehicle form and back. Ignored on robots with no forged
        // vehicle clips, so the key is simply inert rather than half-working.
        bool morph = Input.GetKeyDown(KeyCode.V) || (touch != null && touch.ConsumeMorph());
        if (morph && _vehicle != null)
            _vehicle.Toggle();

        HandleWeaponSwitch(touch);

        Transform aim = _motor.head != null ? _motor.head : transform;

        if (armed)
        {
            bool firing = touch != null ? touch.Fire : Input.GetMouseButton(0);
            var weapon = ActiveWeapon();
            if (weapon != null && firing)
                weapon.TryFire(aim.forward);

            if (scope != null)
                scope.SetScoped(touch != null ? touch.Scope : Input.GetMouseButton(1));
        }
        else if (scope != null)
        {
            scope.SetScoped(false);
        }
    }

    /// <summary>
    /// Debug hook (WeaponDebugConsole): hold a specific slot of the current
    /// usable set — the console grants the weapon through the loadout first.
    /// </summary>
    public void ForceWeapon(int slot)
    {
        RefreshLoadout();
        SetActiveWeapon(slot);
    }

    void HandleWeaponSwitch(TouchControls touch)
    {
        if (weapons == null) return;
        // While the weapon debug console is mid-entry (first digit typed),
        // digits belong to it — don't also flip the player's weapon.
        bool debugCapturing = WeaponDebugConsole.Instance != null
            && WeaponDebugConsole.Instance.AwaitingSecondDigit;

        // Slot 1 and 2 are the basics; slot 3 appears while an airdropped
        // Weapon Pod is running. Scroll and Q/E cycle the same short list.
        if (!debugCapturing)
        {
            for (int i = 0; i < weapons.Length && i < 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    SetActiveWeapon(i);
        }

        int cycle = 0;
        float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollDelta) > 0.01f)
            cycle = scrollDelta > 0 ? 1 : -1;
        if (Input.GetKeyDown(KeyCode.E)) cycle = 1;
        if (Input.GetKeyDown(KeyCode.Q)) cycle = -1;
        if (touch != null)
        {
            int touchCycle = touch.ConsumeWeaponCycle();
            if (touchCycle != 0) cycle = touchCycle;
        }
        if (cycle != 0)
            SetActiveWeapon((_activeWeapon + cycle + weapons.Length) % weapons.Length);
    }

    void SetActiveWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0)
        {
            _activeWeapon = 0;
            return;
        }
        // All weapons share the blaster GameObject; only the active one is ever
        // fired (idle weapons do nothing until their TryFire is called), so we
        // just move the selection index — no GameObject toggling needed.
        _activeWeapon = Mathf.Clamp(index, 0, weapons.Length - 1);
    }

    Weapon ActiveWeapon() =>
        (weapons != null && weapons.Length > 0) ? weapons[_activeWeapon] : null;
}
