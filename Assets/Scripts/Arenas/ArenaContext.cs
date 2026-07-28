using UnityEngine;

/// <summary>
/// The active arena, and the one place anything else asks about arena shape.
///
/// Before this existed the arena's dimensions were literals in six different
/// files — cover churn bounds, treasure drop extents, the reinforcement line,
/// spawn keep-out lists. Each of those now reads through here, so an arena
/// swap moves all of them together.
/// </summary>
public static class ArenaContext
{
    static ArenaDefinition _current;

    public static ArenaDefinition Current
    {
        get => _current ?? (_current = ArenaLibrary.Get(0));
        set => _current = value;
    }

    /// <summary>Half-extent of the area treasure drops may land in.</summary>
    public static Vector2 HalfExtent => Current.HalfExtent;

    /// <summary>Half-extent dynamic cover blocks may move within.</summary>
    public static float CoverHalfExtent => Current.CoverHalfExtent;

    /// <summary>The plane dynamic cover rests on.</summary>
    public static float GroundY => Current.GroundY;

    /// <summary>Spawn points and props that cover and loot must stay clear of.</summary>
    public static Vector3[] KeepOut => Current.KeepOut;

    /// <summary>Floor heights treasure drops may target.</summary>
    public static float[] DropPlanes => Current.DropPlanes;

    public static Vector3 ReinforcementSpawn(int teamId) => Current.ReinforcementLine(teamId);
}
