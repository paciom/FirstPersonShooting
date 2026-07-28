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
                    // HANGAR is the ONE neon arena. Everything after it is built
                    // from a different material family — see ArenaMaterials.
                    new HangarArena(),      // neon sci-fi, panelled and lit
                    new FoundryArena(),     // basalt, riveted iron, tread plate
                    new ZigguratArena(),    // sandstone brick, strata bedrock
                    new BiodomeArena(),     // moss, fleshy stalks, bioluminescence
                    new CrystalHollowArena(), // rough cave rock, faceted crystal
                    new ToyboxArena(),      // grained wood, glossy plastic
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
