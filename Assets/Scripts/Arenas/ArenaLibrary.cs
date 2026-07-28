using UnityEngine;

/// <summary>
/// The arenas the game ships with, in select-screen order. HANGAR stays at
/// index 0: it is the arena the scene is authored as, so it is what the game
/// falls back to whenever a selection is missing or invalid.
///
/// Adding an arena is adding one file and one line here.
/// </summary>
public static class ArenaLibrary
{
    static ArenaDefinition[] _all;

    public static ArenaDefinition[] All
    {
        get
        {
            if (_all == null)
            {
                _all = new ArenaDefinition[]
                {
                    new HangarArena(),
                    new GridspaceArena(),
                    new FoundryArena(),
                    new ZigguratArena(),
                    new ToyboxArena(),
                };
            }
            return _all;
        }
    }

    public static int Count => All.Length;

    public static ArenaDefinition Get(int index)
    {
        var all = All;
        if (all.Length == 0)
            return null;
        return all[Mathf.Clamp(index, 0, all.Length - 1)];
    }
}
