using UnityEngine;

/// <summary>
/// Battle or training range, for the PLAYER v AI card.
///
/// ONE CARD, TWO MODES — the same argument as <see cref="TankRaidPick"/>: a
/// second home-screen tile reading "TRAINING RANGE" would spend a card slot
/// on something the robot-select screen can say in two chips, and the home
/// screen is the one surface where space is genuinely scarce.
///
/// In the training range the match is the ordinary Player v AI arena in every
/// respect — same bots, same airdrops, same living cover — except that no bot
/// ever fires a shot. Enemies walk, chase, loot and transform, which makes
/// them honest moving targets, but nothing shoots back while you learn a gun.
///
/// Remembered between runs, read straight off the store the way TankRaidPick
/// is, so a recompile wiping statics cannot flip it.
/// </summary>
public static class TrainingPick
{
    const string PrefsKey = "PhotonArena.GunfightTraining";

    /// <summary>True when the next Player v AI match is a training range.
    /// Defaults to the real fight — training is the thing you opt into.</summary>
    public static bool Training
    {
        get => PlayerPrefs.GetInt(PrefsKey, 0) != 0;
        set => PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
    }
}
