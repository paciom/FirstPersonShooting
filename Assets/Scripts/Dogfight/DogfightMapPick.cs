using UnityEngine;

/// <summary>The three skies DOGFIGHT can be flown in. The order is the
/// picker's order; Airfield is the original set and the default.</summary>
public enum DogfightMapKind
{
    Airfield = 0,
    TinyPlanet = 1,
    DonutStation = 2,
}

/// <summary>
/// Which battlefield DOGFIGHT loads — both cards. Remembered between sorties
/// (PlayerPrefs), read straight off the store on every access the way
/// <see cref="DogfightTeamSize"/> is, so there is no cached copy to go stale
/// when a recompile wipes statics.
/// </summary>
public static class DogfightMapPick
{
    public static readonly DogfightMapKind[] All =
    {
        DogfightMapKind.Airfield,
        DogfightMapKind.TinyPlanet,
        DogfightMapKind.DonutStation,
    };

    const string PrefsKey = "PhotonArena.DogfightMap";

    public static DogfightMapKind Chosen
    {
        get => (DogfightMapKind)Mathf.Clamp(
            PlayerPrefs.GetInt(PrefsKey, 0), 0, All.Length - 1);
        set => PlayerPrefs.SetInt(PrefsKey, (int)value);
    }

    public static string NameOf(DogfightMapKind kind) => kind switch
    {
        DogfightMapKind.TinyPlanet => "TINY  PLANET",
        DogfightMapKind.DonutStation => "DONUT  STATION",
        _ => "AIRFIELD",
    };
}
