using UnityEngine;

/// <summary>
/// Who drives in TANK RAID.
///
/// ONE CARD, TWO MODES. The alternative — a second menu tile reading "TANK
/// RAID: AI v AI" — spends a slot on the home screen to say something the
/// select screen can say in two chips, and the home screen is the one surface
/// in this game where space is genuinely scarce. So the variant moved onto the
/// screen that was already asking questions about the run.
///
/// Remembered between runs, and read straight off the store on every access the
/// way <see cref="DogfightMapPick"/> is — there is no cached copy here to go
/// stale when a recompile wipes statics.
/// </summary>
public static class TankRaidPick
{
    const string PrefsKey = "PhotonArena.TankRaidPlayerDrives";

    /// <summary>True when the player has the controls. Defaults to yes: a mode
    /// opened for the first time should hand over the sticks, not put on a
    /// demonstration.</summary>
    public static bool PlayerDrives
    {
        get => PlayerPrefs.GetInt(PrefsKey, 1) != 0;
        set => PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
    }
}
