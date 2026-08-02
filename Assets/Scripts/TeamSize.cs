using UnityEngine;

/// <summary>
/// How many robots each side fields in the two Gunfight modes — AI v AI and
/// Player v AI. Remembered between matches (PlayerPrefs), read straight off it
/// on every access the way <see cref="BrawlDifficulty"/> does, so there is no
/// cached copy to go stale when a recompile wipes the statics mid-Play.
///
/// A size counts FIGHTERS, not bots: in Player v AI the player is one of the
/// cyan robots, so "1 v 1" is the player alone against one enemy rather than
/// the player plus a full squad. See <see cref="TeamRoster"/>, which is what
/// actually stands them on the field.
/// </summary>
public static class TeamSize
{
    /// <summary>The one-click matchups. Anything else is typed into the box.</summary>
    public static readonly int[] Presets = { 1, 2, 4, 10 };

    public const int Min = 1;

    /// <summary>
    /// Ceiling on the typed amount. Not a technical limit — 32 robots is simply
    /// where a browser build stops holding 60fps, and the arenas are ~32 units
    /// across, so more than this is a scrum rather than a battle.
    /// </summary>
    public const int Max = 16;

    /// <summary>The scene's own cast: three bots per team, as built.</summary>
    public const int Default = 3;

    const string PrefsKey = "PhotonArena.TeamSize";

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

    /// <summary>"4  v  4", spaced for this project's all-caps UI.</summary>
    public static string Matchup(int size) => $"{size}  v  {size}";

    /// <summary>
    /// What the chosen size actually puts on the field, said in full — the one
    /// place the "the player is one of the robots" rule is spelled out.
    /// </summary>
    public static string Describe(int size, bool playerPlays)
    {
        if (!playerPlays)
            return $"{size}  ROBOT{(size == 1 ? "" : "S")}  A  SIDE";

        int allies = Mathf.Max(0, size - 1);
        string cyan = allies == 0 ? "YOU  ALONE"
            : allies == 1 ? "YOU  +  1  ALLY"
            : $"YOU  +  {allies}  ALLIES";
        return $"{cyan}   v   {size}  ENEM{(size == 1 ? "Y" : "IES")}";
    }
}
