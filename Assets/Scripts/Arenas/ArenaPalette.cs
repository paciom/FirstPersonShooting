using UnityEngine;

/// <summary>
/// Every colour and atmosphere decision an arena makes, in one struct.
///
/// Emissive values live under the project's bloom ceiling: anything much past
/// 2.5 washes to white once the volume's bloom gets hold of it, which is how a
/// carefully chosen accent colour turns into a grey smear on camera.
/// </summary>
public struct ArenaPalette
{
    /// <summary>Main ground surface.</summary>
    public Color floor;

    /// <summary>Perimeter and structural surfaces.</summary>
    public Color wall;

    /// <summary>The two glow colours the arena trims itself with.</summary>
    public Color accentA;
    public Color accentB;

    /// <summary>Flat ambient — the floor level every surface is read against.</summary>
    public Color ambient;

    public Color keyLight;
    public float keyIntensity;

    /// <summary>Background where nothing is drawn. Also the fog colour's anchor.</summary>
    public Color sky;

    /// <summary>Exponential-squared fog. Density 0 disables fog entirely.</summary>
    public Color fog;
    public float fogDensity;

    /// <summary>
    /// Bloom intensity for this arena. The house default is 2.2; bright, flat
    /// arenas (TOYBOX) need far less or every surface blooms into every other.
    /// </summary>
    public float bloom;

    /// <summary>The house look — used as a starting point by most arenas.</summary>
    public static ArenaPalette Default => new ArenaPalette
    {
        floor = new Color(0.12f, 0.13f, 0.16f),
        wall = new Color(0.16f, 0.18f, 0.23f),
        accentA = new Color(0.2f, 0.9f, 1f),
        accentB = new Color(1f, 0.25f, 0.9f),
        ambient = new Color(0.30f, 0.33f, 0.42f),
        keyLight = new Color(0.75f, 0.8f, 1f),
        keyIntensity = 1.3f,
        sky = new Color(0.02f, 0.03f, 0.05f),
        fog = new Color(0.05f, 0.07f, 0.12f),
        fogDensity = 0f,
        bloom = 2.2f,
    };
}
