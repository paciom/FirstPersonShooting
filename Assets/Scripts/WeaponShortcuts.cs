using UnityEngine;

/// <summary>
/// The four guns a player keeps under their thumb.
///
/// WHY. Player v AI hands out the whole catalogue — sixty weapons — and the rack
/// that makes sixty findable is a modal that covers the screen. Opening it mid
/// firefight to change gun is not a choice anyone makes twice. So the rack is
/// for CHOOSING and these are for SWITCHING: pick four at the start, then flick
/// between them with one tap for the rest of the match.
///
/// FILLED IN ORDER, NOT ASSIGNED TO A SLOT. The player never says "put this in
/// slot 3" — they pick a gun and it lands in the next empty slot, which is the
/// one already highlighted. Four picks and the bar is built. That is one
/// decision per weapon instead of two, and it is the difference between a
/// seven-year-old setting this up and a seven-year-old skipping it.
///
/// Lives on the player rather than in a static so it dies with them, and is
/// cleared at the top of every match: a bar carried over from the last game is a
/// bar the player did not choose.
/// </summary>
public class WeaponShortcuts : MonoBehaviour
{
    /// <summary>How many guns fit on the bar. Four is a hand.</summary>
    public const int SlotCount = 4;

    readonly Weapon[] _slots = new Weapon[SlotCount];

    /// <summary>Bumped whenever the bar changes, so the UI knows to re-read it.</summary>
    public int Version { get; private set; }

    public static WeaponShortcuts Of(Component owner) =>
        owner != null ? owner.GetComponent<WeaponShortcuts>() : null;

    /// <summary>The weapon on a slot, or null while it is still empty.</summary>
    public Weapon Get(int slot) =>
        slot >= 0 && slot < SlotCount ? _slots[slot] : null;

    /// <summary>
    /// The slot the next pick will fill, or -1 once all four are set. This is
    /// the one the bar highlights — the whole selection flow is "the glowing
    /// one is where your next choice goes".
    /// </summary>
    public int NextEmpty
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] == null)
                    return i;
            return -1;
        }
    }

    /// <summary>True while the player still has slots to fill.</summary>
    public bool Choosing => NextEmpty >= 0;

    /// <summary>Which slot holds this weapon, or -1.</summary>
    public int SlotOf(Weapon weapon)
    {
        if (weapon == null)
            return -1;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] == weapon)
                return i;
        return -1;
    }

    /// <summary>
    /// Put a weapon on the next empty slot. Returns the slot it took, or -1 when
    /// it took none.
    ///
    /// A weapon already on the bar takes none — picking the Frostbite Beam twice
    /// should not spend two of your four on it, and the player who does that is
    /// far likelier to have forgotten it was already there than to have wanted
    /// it twice.
    /// </summary>
    public int Assign(Weapon weapon)
    {
        if (weapon == null || SlotOf(weapon) >= 0)
            return -1;
        int slot = NextEmpty;
        if (slot < 0)
            return -1;

        _slots[slot] = weapon;
        Version++;
        return slot;
    }

    /// <summary>
    /// Empty the bar and start the selection over. The only way to change a
    /// choice, deliberately: reassigning one slot at a time needs a way to say
    /// WHICH slot, and that is the second decision per weapon this whole design
    /// exists to avoid.
    /// </summary>
    public void Clear()
    {
        bool had = false;
        for (int i = 0; i < SlotCount; i++)
        {
            had |= _slots[i] != null;
            _slots[i] = null;
        }
        if (had)
            Version++;
    }
}
