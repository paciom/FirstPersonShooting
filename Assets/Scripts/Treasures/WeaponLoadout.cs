using UnityEngine;

/// <summary>
/// What a character is actually allowed to shoot right now.
///
/// Every character still carries all 54 weapon components (they're attached
/// once by ArenaBuilder — adding components at runtime isn't practical), but
/// only the two BASIC guns are unlocked. Everything else is treasure-only: a
/// Weapon Pod parachutes in, whoever grabs it gets one random arsenal weapon
/// for a limited time, and losing a fight loses the gun.
///
/// The timer is deliberate rather than an ammo count: the 54 weapons range from
/// single-shot rails to continuous beams and there's no shared "a shot was
/// fired" signal to count against, so seconds are the one currency that means
/// the same thing for all of them (and reads clearly on a countdown).
///
/// PlayerBrain and AIBrain both drive themselves off <see cref="Available"/>
/// and re-read it whenever <see cref="Version"/> changes.
/// </summary>
public class WeaponLoadout : MonoBehaviour
{
    [Tooltip("Canonical arsenal in debug-console order; index i is weapon number i+1.")]
    public Weapon[] all;

    [Tooltip("Indices into `all` that this character always has (the two basics).")]
    public int[] basicIndices = { 0 };

    [Tooltip("The guns used in vehicle form, replacing the whole arsenal. " +
             "Empty falls back to the first basic.")]
    public Weapon[] vehicleWeapons;

    [Tooltip("Seconds a treasure weapon stays equipped.")]
    public float grantDuration = 22f;

    // Built lazily rather than only in Awake: Unity gives no ordering guarantee
    // between components on one object, and both brains read the loadout from
    // their own Awake.
    Weapon[] _available = new Weapon[0];
    int _version;
    bool _built;

    /// <summary>Bumped whenever the usable set changes, so brains know to re-read it.</summary>
    public int Version { get { EnsureBuilt(); return _version; } }

    /// <summary>Basics first (stable slots), then the granted treasure weapon if any.</summary>
    public Weapon[] Available { get { EnsureBuilt(); return _available; } }

    /// <summary>
    /// Whether this match arms everyone with everything.
    ///
    /// STATIC, not a field per character, because characters are BORN mid-match:
    /// TeamRoster sizes the teams by cloning, and RobotReinforcements buys more
    /// robots out of the team bank. A flag set on each loadout at match start
    /// would be missed by every one of them — Unity does not serialize
    /// auto-property backing fields, so a clone would not even inherit its
    /// template's. Read at <see cref="Rebuild"/> time instead, which every
    /// loadout runs from its own Awake, so a robot built five minutes in comes
    /// up armed the same as one that started the match.
    /// </summary>
    public static bool FullArsenalMatch { get; private set; }

    /// <summary>
    /// Turn the whole arsenal on or off for the match, and re-arm everyone
    /// already standing. Called by GameModeController on the way into a mode.
    /// </summary>
    public static void SetFullArsenalMatch(bool on)
    {
        if (FullArsenalMatch == on)
            return;
        FullArsenalMatch = on;

        foreach (var loadout in FindObjectsByType<WeaponLoadout>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
            if (loadout != null)
                loadout.Rebuild();
    }

    /// <summary>The treasure weapon currently equipped, or null.</summary>
    public Weapon Special { get; private set; }

    /// <summary>
    /// Index of the treasure weapon inside <see cref="Available"/>, or -1.
    ///
    /// Searched rather than assumed to be last: that only holds when the usable
    /// set is "basics plus the grant", and a full-arsenal match carries every
    /// weapon in catalogue order.
    /// </summary>
    public int SpecialSlot => Special != null ? SlotOf(Special) : -1;

    /// <summary>Seconds of treasure weapon left; infinite while the debug console holds it.</summary>
    public float SpecialSecondsLeft =>
        Special == null ? 0f : Mathf.Max(0f, _specialUntil - Time.time);

    public int TotalWeapons => all != null ? all.Length : 0;

    /// <summary>
    /// Vehicle form: the mounted siege kit instead of the arsenal. Set by
    /// <see cref="TransformMode"/>. Two slow, heavy guns in exchange for the
    /// whole loadout — including any treasure weapon, which stays equipped and
    /// comes back the moment the robot stands up. Driving is a different way to
    /// fight rather than a worse one.
    /// </summary>
    public bool VehicleMode { get; private set; }

    /// <summary>True when this character has a dedicated vehicle kit.</summary>
    public bool HasVehicleWeapons
    {
        get
        {
            if (vehicleWeapons == null)
                return false;
            foreach (var weapon in vehicleWeapons)
                if (weapon != null)
                    return true;
            return false;
        }
    }

    public void SetVehicleMode(bool vehicle)
    {
        if (VehicleMode == vehicle)
            return;
        VehicleMode = vehicle;
        Rebuild();
    }

    float _specialUntil;
    EnergyShield _shield;

    public static WeaponLoadout Of(Transform root) =>
        root != null ? root.GetComponent<WeaponLoadout>() : null;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        if (_shield != null)
            _shield.OnDeRezzed += ClearSpecial;   // de-rezzing drops the treasure gun
        EnsureBuilt();
    }

    void EnsureBuilt()
    {
        if (_built)
            return;
        _built = true;
        Rebuild();
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= ClearSpecial;
    }

    void Update()
    {
        if (Special != null && Time.time >= _specialUntil)
            ClearSpecial();
    }

    // ------------------------------------------------------------------ grants

    /// <summary>Equip a random treasure weapon. Returns it (null if the arsenal is empty).</summary>
    public Weapon GrantRandom() => Grant(RandomTreasureIndex(), grantDuration);

    /// <summary>
    /// Equip weapon <paramref name="index"/> from <see cref="all"/> for
    /// <paramref name="duration"/> seconds. Basics are already usable, so
    /// granting one is a no-op that just returns it.
    /// </summary>
    public Weapon Grant(int index, float duration)
    {
        if (all == null || index < 0 || index >= all.Length || all[index] == null)
            return null;
        if (IsBasic(index))
            return all[index];
        // Already carrying it, along with everything else. Handed back without
        // becoming the Special, so the HUD doesn't start a countdown on a gun
        // that cannot expire.
        if (FullArsenalMatch)
            return all[index];

        Special = all[index];
        _specialUntil = Time.time + duration;
        Rebuild();
        return Special;
    }

    public void ClearSpecial()
    {
        if (Special == null)
            return;
        Special = null;
        _specialUntil = 0f;
        Rebuild();
    }

    /// <summary>A random index in <see cref="all"/> that isn't one of the basics.</summary>
    public int RandomTreasureIndex()
    {
        if (all == null || all.Length == 0)
            return -1;
        // Bounded retry rather than building a candidate list every pickup:
        // with 2 basics out of 54 a hit is near-certain on the first roll.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int i = Random.Range(0, all.Length);
            if (!IsBasic(i) && all[i] != null)
                return i;
        }
        for (int i = 0; i < all.Length; i++)
            if (!IsBasic(i) && all[i] != null)
                return i;
        return -1;
    }

    public bool IsBasic(int index)
    {
        if (basicIndices == null)
            return false;
        foreach (int b in basicIndices)
            if (b == index)
                return true;
        return false;
    }

    /// <summary>Where a weapon sits in <see cref="Available"/>, or -1 if it isn't usable.</summary>
    public int SlotOf(Weapon weapon)
    {
        var available = Available;
        for (int i = 0; i < available.Length; i++)
            if (available[i] == weapon)
                return i;
        return -1;
    }

    void Rebuild()
    {
        _built = true;

        // Vehicle form: the mounted siege kit. Special is left untouched (and
        // its timer keeps running) so unfolding restores it.
        //
        // Falls back to the first basic for characters built without a vehicle
        // kit, which is how it behaved before the kit existed.
        if (VehicleMode)
        {
            _available = HasVehicleWeapons ? vehicleWeapons : FirstBasic();
            _version++;
            return;
        }

        // Everything, in catalogue order — which is also the order the picker's
        // tabs walk, so slot numbers and the rack agree.
        if (FullArsenalMatch)
        {
            int carried = 0;
            if (all != null)
                foreach (var weapon in all)
                    if (weapon != null)
                        carried++;

            var everything = new Weapon[carried];
            int next = 0;
            if (all != null)
                foreach (var weapon in all)
                    if (weapon != null)
                        everything[next++] = weapon;

            _available = everything;
            _version++;
            return;
        }

        int basics = 0;
        if (all != null && basicIndices != null)
            foreach (int b in basicIndices)
                if (b >= 0 && b < all.Length && all[b] != null)
                    basics++;

        var list = new Weapon[basics + (Special != null ? 1 : 0)];
        int slot = 0;
        if (all != null && basicIndices != null)
            foreach (int b in basicIndices)
                if (b >= 0 && b < all.Length && all[b] != null)
                    list[slot++] = all[b];
        if (Special != null)
            list[slot] = Special;

        _available = list;
        _version++;
    }

    /// <summary>The vehicle's one gun: the first basic that actually exists.</summary>
    Weapon[] FirstBasic()
    {
        if (all != null && basicIndices != null)
            foreach (int b in basicIndices)
                if (b >= 0 && b < all.Length && all[b] != null)
                    return new[] { all[b] };
        return new Weapon[0];
    }
}
