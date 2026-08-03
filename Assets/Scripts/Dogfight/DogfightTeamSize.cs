using UnityEngine;

/// <summary>
/// How many jets each side fields in DOGFIGHT — both cards. Remembered
/// between sorties (PlayerPrefs), read straight off it on every access the
/// way <see cref="TeamSize"/> and <see cref="BrawlDifficulty"/> are, so
/// there is no cached copy to go stale when a recompile wipes statics.
///
/// Its own store rather than the Gunfight modes' <see cref="TeamSize"/>:
/// the arena comfortably holds ten robots a side, but every jet here is a
/// missile-slinging, triple-form pawn with two full stage sets — eight a
/// side is where the sky stops reading and the browser stops keeping up.
/// The wording helpers are shared; only the numbers are this mode's own.
/// </summary>
public static class DogfightTeamSize
{
    /// <summary>The one-click matchups. Anything else is typed into the box.</summary>
    public static readonly int[] Presets = { 1, 2, 4 };

    public const int Min = 1;
    public const int Max = 8;
    public const int Default = 1;

    const string PrefsKey = "PhotonArena.DogfightTeamSize";

    public static int PerTeam
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, Default), Min, Max);
        set => PlayerPrefs.SetInt(PrefsKey, Mathf.Clamp(value, Min, Max));
    }

    public static bool IsPreset(int size)
    {
        foreach (int preset in Presets)
            if (preset == size)
                return true;
        return false;
    }
}
