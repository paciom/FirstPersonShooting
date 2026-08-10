using UnityEngine;

/// <summary>
/// Human input → CharacterMotor + Weapon. LMB fires the held weapon, RMB
/// engages the X-Ray scope, T folds into tank form and back, F throws the
/// martial-arts dash combo. Uses the legacy Input API for now (active input
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
    SniperScope _sniper;
    AimAssist _assist;
    MartialArts _martial;
    int _loadoutVersion = -1;
    int _activeWeapon;
    bool _warnedNoVehicle;

    public string CurrentWeaponName =>
        (weapons != null && weapons.Length > 0 && weapons[_activeWeapon] != null)
            ? weapons[_activeWeapon].weaponName : "";

    /// <summary>
    /// Which slot of <see cref="weapons"/> is in hand. Read by the on-screen
    /// weapon panel, which has to mark the one you are already holding.
    /// </summary>
    public int ActiveSlot => _activeWeapon;

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
        // Built here rather than in the scene so it needs no rebuild.
        _sniper = GetComponent<SniperScope>();
        if (_sniper == null)
            _sniper = gameObject.AddComponent<SniperScope>();
        // After the scope: the assist reads it to know when it is wanted.
        _assist = GetComponent<AimAssist>();
        if (_assist == null)
            _assist = gameObject.AddComponent<AimAssist>();
        _martial = GetComponent<MartialArts>();
        if (_martial == null)
            _martial = gameObject.AddComponent<MartialArts>();
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

    /// <summary>
    /// The brain switches off with the match — never leave a zoomed camera or a
    /// live x-ray behind for the menu to render through.
    /// </summary>
    void OnDisable()
    {
        if (_sniper != null) _sniper.SetScoped(false);
        if (scope != null) scope.SetScoped(false);
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

        // Zoomed in, the same swipe has to cover far less ground — a scope you
        // can't hold steady is worse than no scope.
        float lookScale = _sniper != null ? _sniper.LookScale : 1f;

        // One look delta from either scheme, then one place the assist reads
        // it. Correcting each branch separately is how a stick and a mouse end
        // up aiming by different rules.
        Vector2 look = Vector2.zero;
        if (touch != null)
        {
            look = touch.LookDelta * lookScale;
        }
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            look = new Vector2(
                Input.GetAxis("Mouse X") * mouseSensitivity,
                Input.GetAxis("Mouse Y") * mouseSensitivity) * lookScale;
        }

        // Scoped, the computer does the last few degrees — see AimAssist for
        // what keeps that from taking the aim away from the player. Fed through
        // AddLook like any other input, so freeze, ice and the vehicle rules all
        // still apply to an assisted turn.
        if (_assist != null)
            look = _assist.Steer(look, Time.deltaTime);
        if (look != Vector2.zero)
            _motor.AddLook(look);

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

        // T folds into vehicle form and back — V still does it too, which is
        // where the control used to live. Ignored on robots with no forged
        // vehicle clips, so the key is simply inert rather than half-working.
        bool morph = Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.V)
            || (touch != null && touch.ConsumeMorph());
        if (morph && _vehicle != null)
        {
            // Say it once: an inert key and a broken key look identical from
            // the outside, and this is the first thing to check when the fold
            // (and with it the corner replay) never happens.
            if (!_vehicle.CanTransform && !_warnedNoVehicle)
            {
                _warnedNoVehicle = true;
                Debug.LogWarning("[PlayerBrain] This robot has no forged vehicle clips — "
                    + "transform does nothing. Run Photon Arena → Forge Robot Transform Clips.");
            }
            _vehicle.Toggle();
            // Read the form back rather than assuming the toggle took: a fold
            // already in progress ignores it, and telling the other client we
            // transformed when we didn't puts the two arenas out of step.
            if (_vehicle.CanTransform)
                NetMatch.NotifyLocalTransform(_vehicle.IsVehicle);
        }

        // Z zooms the sniper scope in and back out; it stacks with the X-Ray
        // scope, which reveals but does not magnify.
        if (_sniper != null
            && (Input.GetKeyDown(KeyCode.Z) || (touch != null && touch.ConsumeSnipe())))
            _sniper.Toggle();

        // F throws the martial-arts combo: a forward lunge with close-range
        // strikes. MartialArts owns every refusal (cooldown, tank form, ice).
        if (_martial != null
            && (Input.GetKeyDown(KeyCode.F) || (touch != null && touch.ConsumeMartial())))
            _martial.TryStrike();

        HandleWeaponSwitch(touch);

        Transform aim = _motor.head != null ? _motor.head : transform;

        // Driving: the turret follows the view. The hull yaws with the mouse
        // too, so the barrel mostly sits dead ahead — but it is what keeps the
        // gun pointing where the shots go while the hull is still catching up
        // after a flick, and it is the same call the bots make.
        if (_vehicle != null && _vehicle.IsVehicle)
            _vehicle.Turret.AimAlong(aim.forward);

        if (armed)
        {
            bool firing = touch != null ? touch.Fire : Input.GetMouseButton(0);
            var weapon = ActiveWeapon();
            if (weapon != null && firing)
            {
                weapon.TryFire(aim.forward);
                // Online PvP replicates fire intent, not hits — the other
                // client replays this on the mirror pawn's own weapons.
                NetMatch.NotifyLocalFire(_activeWeapon, aim.forward);
            }

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

        // The number keys mean the SHORTCUT BAR wherever one has been built,
        // and the raw slot list otherwise.
        //
        // Which is the whole point of the bar: with sixty weapons carried, keys
        // 1-9 reaching catalogue entries one to nine is an accident of ordering,
        // not a control — nobody wants "the ninth weapon", they want the four
        // they chose. Where no bar exists (every other mode, two basics and a
        // pod) the old meaning is the only one that makes sense.
        var shortcuts = WeaponShortcuts.Of(this);
        if (!debugCapturing)
        {
            for (int i = 0; i < 9; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + i))
                    continue;
                // Per KEY, not per mode: the bar wins on the keys it has filled
                // and the slot list keeps the rest, so a half-built bar leaves
                // 3 and 4 doing what they always did rather than going dead.
                if (shortcuts != null && i < WeaponShortcuts.SlotCount
                    && shortcuts.Get(i) != null)
                    Equip(shortcuts.Get(i));
                else if (i < weapons.Length)
                    SetActiveWeapon(i);
            }
        }

        // TAB opens the same rack the ARMS button does. In Player v AI the
        // whole catalogue is carried, and nine number keys and a pair of cycle
        // arrows are no way to find one gun in sixty.
        if (Input.GetKeyDown(KeyCode.Tab))
            TouchControls.ToggleWeaponRack();

        // A gun tapped on the shortcut bar is the most absolute choice of all —
        // it names the weapon rather than a position in a list that shifts.
        if (touch != null && shortcuts != null)
        {
            int slot = touch.ConsumeShortcutPick();
            if (slot >= 0 && Equip(shortcuts.Get(slot)))
                return;
        }

        // A slot tapped in the on-screen weapon panel is an absolute choice,
        // so it wins over any cycling in the same frame.
        if (touch != null)
        {
            int picked = touch.ConsumeWeaponPick();
            if (picked >= 0)
            {
                SetActiveWeapon(picked);
                // Choosing from the rack is also how the bar gets built: the
                // gun you just picked drops into the slot that was glowing, and
                // the glow moves along. Assign refuses a duplicate and refuses
                // once the bar is full, so a mid-match rack visit is just a
                // weapon switch — which is what it should be by then.
                if (shortcuts != null)
                    shortcuts.Assign(ActiveWeapon());
                return;
            }
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

    /// <summary>
    /// Take a specific weapon into hand. Returns false when this character is
    /// not carrying it — which is how a shortcut bar built in one match behaves
    /// harmlessly in a mode that hands out fewer guns.
    /// </summary>
    public bool Equip(Weapon weapon)
    {
        if (weapon == null || weapons == null)
            return false;
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] != weapon)
                continue;
            SetActiveWeapon(i);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The gun in hand. Public because the first-person viewmodel is built on
    /// it — the prop hanging off the camera has to be whichever weapon this is,
    /// and the muzzle light it flashes is what shakes it.
    /// </summary>
    public Weapon ActiveWeapon() =>
        (weapons != null && weapons.Length > 0) ? weapons[_activeWeapon] : null;
}
