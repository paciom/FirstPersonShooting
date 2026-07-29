using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The war's paper trail: a short per-team feed of notable acts — structures
/// raised and lost, robots deployed, waves launched — that the spectator
/// panels read out so an audience can follow both commanders' hands.
///
/// Static ring buffers, cleared per session; a recompile during Play empties
/// the feed and the next act refills it, which for a log is exactly right.
/// </summary>
public static class CommanderOps
{
    public struct Entry
    {
        public float time;
        public string message;
    }

    const int Keep = 8;

    static readonly List<Entry>[] Logs = { new List<Entry>(), new List<Entry>() };

    public static void Log(int teamId, string message)
    {
        if (teamId < 0 || teamId >= Logs.Length || string.IsNullOrEmpty(message))
            return;
        var log = Logs[teamId];
        log.Add(new Entry { time = Time.time, message = message });
        if (log.Count > Keep)
            log.RemoveAt(0);
    }

    public static IReadOnlyList<Entry> Of(int teamId) =>
        teamId >= 0 && teamId < Logs.Length ? Logs[teamId] : Logs[0];

    /// <summary>Fresh war, fresh page. Called on every session start.</summary>
    public static void Clear()
    {
        foreach (var log in Logs)
            log.Clear();
    }
}
