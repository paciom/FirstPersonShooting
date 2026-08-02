using UnityEngine;

/// <summary>
/// The battlefield that never ends.
///
/// A fixed ring of bands, recycled: when the band furthest behind the hero falls
/// out of sight it is moved to the front of the queue rather than destroyed and
/// rebuilt, so the field is infinite and the object count is constant. The same
/// trick <see cref="ChineseRunRoad"/> runs on, and for the same reason — a
/// scroller that instantiates as it goes spends its third minute in the garbage
/// collector.
///
/// WHERE THIS ONE DIFFERS. That road is bare, because its four lanes are the
/// question and anything else on them is noise. This is a battlefield: the eye
/// needs something standing still to read the scroll against, and a tank needs
/// something to put between itself and a gun. So every band gets seeded scenery
/// — blocks, ridges, crystal — from its own index, which means band 47 is the
/// same band 47 every time it comes round and the recycling stays invisible.
///
/// Rocks carry colliders and bolts stop on them. The deck does not: the ground
/// is flat at y = 0, every pawn writes its own y, and a floor collider would
/// only ever be something for a low shot to bury itself in.
/// </summary>
public class TankField : MonoBehaviour
{
    /// <summary>
    /// Half-width of the drivable strip.
    ///
    /// Set against <see cref="TankRaidCamera"/>, not chosen: that camera's view
    /// is a trapezoid roughly 20 m wide at the bottom of the screen and 36 at
    /// the top, so a strip any wider than this would have its fence off screen
    /// exactly where the hero drives — and an invisible wall the player keeps
    /// hitting is the worst thing a scroller can have. Fifteen keeps both berms
    /// in shot at the narrow end, including on a 4:3 display.
    /// </summary>
    public const float HalfWidth = 15f;

    /// <summary>
    /// How far behind the frontier the hero may fall before it is pushed. Also
    /// set against the camera: the bottom of the screen is about 11 m behind the
    /// frontier, so a longer leash would let the hero reverse out of shot.
    /// </summary>
    public const float Trail = 9f;

    /// <summary>
    /// Ground reaches well past the fence, because the camera sees well past it
    /// at the top of the screen. Void either side of a narrow ribbon reads as a
    /// bug; a wide plain with a wall across it reads as a battlefield.
    /// </summary>
    const float DeckHalfWidth = 40f;

    const float BandLength = 30f;
    const int BandCount = 14;
    /// <summary>How far behind the hero a band may fall before it is recycled.</summary>
    const float BehindLimit = 60f;

    static readonly Color DeckDark = new Color(0.10f, 0.13f, 0.16f);
    static readonly Color EdgeAmber = new Color(1f, 0.62f, 0.15f);
    static readonly Color RockGrey = new Color(0.22f, 0.24f, 0.29f);
    static readonly Color CrystalTeal = new Color(0.25f, 0.95f, 0.85f);

    /// <summary>
    /// The scroll line. Creeps forward on its own and never retreats, and the
    /// hero is clamped to the strip behind it — see <see cref="Clamp"/>. Static
    /// because the pawns ask for it from their own Update and there is exactly
    /// one field.
    /// </summary>
    public static float Frontier { get; private set; }

    /// <summary>Metres per second the screen advances whether or not the player does.</summary>
    public const float ScrollSpeed = 2.5f;

    Transform[] _bands;
    int _nextBand;
    float _frontEdge;

    public static TankField Build(Transform parent)
    {
        var go = new GameObject("Field");
        go.transform.SetParent(parent, false);
        var field = go.AddComponent<TankField>();
        field.Compose();
        return field;
    }

    /// <summary>
    /// Hold a pawn inside the fence and behind the frontier.
    ///
    /// The back edge is the only wall that matters: it is what turns "drive
    /// wherever" into a scrolling game. A pawn parked against it is being pushed
    /// up the field at <see cref="ScrollSpeed"/> — slowly enough to be a nudge
    /// rather than a shove, but it never stops.
    /// </summary>
    public static Vector3 Clamp(Vector3 position, float radius)
    {
        float lane = Mathf.Max(1f, HalfWidth - radius);
        position.x = Mathf.Clamp(position.x, -lane, lane);
        position.z = Mathf.Max(position.z, Frontier - Trail);
        return position;
    }

    /// <summary>True once a position has fallen off the bottom of the screen —
    /// what raiders and dropped loot are swept on.</summary>
    public static bool FallenBehind(Vector3 position, float margin = 12f) =>
        position.z < Frontier - Trail - margin;

    /// <summary>Start a run. The frontier is absolute, so it has to be reset.</summary>
    public static void ResetFrontier() => Frontier = 0f;

    /// <summary>
    /// Advance the scroll and recycle whatever the hero has left behind. Driven
    /// from the mode rather than from this component's own Update, so the field
    /// can never scroll on during the beat between a hero de-rezzing and
    /// continuing.
    /// </summary>
    public void Advance(float heroZ, float dt)
    {
        Frontier = Mathf.Max(Frontier + ScrollSpeed * dt, heroZ);
        Recycle(heroZ);
    }

    void Recycle(float heroZ)
    {
        foreach (var band in _bands)
        {
            if (band == null)
                continue;
            while (band.position.z + BandLength < heroZ - BehindLimit)
            {
                Reseed(band, _nextBand++);
                band.position = new Vector3(0f, 0f, _frontEdge);
                _frontEdge += BandLength;
            }
        }
    }

    void Compose()
    {
        // The field's own lights. The arena's key light points wherever the
        // arena wanted it, and this mode never sees the arena.
        Sun("KeyLight", new Vector3(46f, 24f, 0f), 1.2f, new Color(1f, 0.96f, 0.9f), true);
        Sun("FillLight", new Vector3(24f, 200f, 0f), 0.4f, new Color(0.5f, 0.7f, 1f), false);

        _bands = new Transform[BandCount];
        // The first bands start behind the hero, so there is ground under the
        // opening frame and ground to be pushed off.
        _frontEdge = -BandLength * 2f;
        for (int i = 0; i < BandCount; i++)
        {
            _bands[i] = new GameObject($"Band{i}").transform;
            _bands[i].SetParent(transform, false);
            BuildDeck(_bands[i]);
            Reseed(_bands[i], _nextBand++);
            _bands[i].position = new Vector3(0f, 0f, _frontEdge);
            _frontEdge += BandLength;
        }
    }

    /// <summary>The permanent half of a band: deck, edge rails, and the marker
    /// stripes that make speed visible. Identical on every band, so recycling
    /// one to the front cannot be seen.</summary>
    void BuildDeck(Transform band)
    {
        var deck = ArenaMaterials.Surface("tank-deck", DeckDark, new Color(0.3f, 0.36f, 0.42f), 6f, 0.35f);
        var rail = ArenaMaterials.Emissive("tank-rail", EdgeAmber, 1.9f);
        var stripe = ArenaMaterials.Emissive("tank-stripe", new Color(0.45f, 0.6f, 0.75f), 0.9f);

        // Top surface at exactly y = 0, which is the floor every pawn writes.
        Box(band, "Deck", new Vector3(0f, -0.5f, BandLength * 0.5f),
            new Vector3(DeckHalfWidth * 2f, 1f, BandLength), deck, collide: false);

        foreach (float side in new[] { -1f, 1f })
        {
            Box(band, "Rail", new Vector3(side * HalfWidth, 0.05f, BandLength * 0.5f),
                new Vector3(0.3f, 0.1f, BandLength), rail, collide: false);
            // A wall on the rail, so the fence is something a shell stops
            // against rather than an invisible line tanks slide along. Tall
            // enough to read from a camera looking down on it at sixty degrees.
            Box(band, "Berm", new Vector3(side * (HalfWidth + 1.4f), 1.2f, BandLength * 0.5f),
                new Vector3(2.4f, 2.4f, BandLength), ArenaMaterials.Lit("tank-berm", RockGrey, 0.2f),
                collide: true);
            // And a broken skyline beyond it, so the ground past the wall is
            // scenery rather than an empty apron.
            Box(band, "Outland", new Vector3(side * (HalfWidth + 12f), 1.6f, BandLength * 0.35f),
                new Vector3(7f, 3.2f, 9f), ArenaMaterials.Lit("tank-outland", RockGrey * 0.75f, 0.15f),
                collide: false);
        }

        for (int i = 0; i < 3; i++)
            Box(band, "Stripe", new Vector3(0f, 0.03f, i * (BandLength / 3f) + BandLength / 6f),
                new Vector3(0.35f, 0.06f, 3.2f), stripe, collide: false);
    }

    /// <summary>
    /// The scenery, rolled from the band's own index.
    ///
    /// SEEDED, NOT RANDOM. A band that re-rolled itself on every recycle would
    /// shimmer as it came round; one that never changed would turn the whole
    /// field into fourteen repeating tiles the eye locks onto within seconds.
    /// Keying the roll to a monotonic index gives an endless field where each
    /// stretch of ground is its own place.
    ///
    /// The centre lane is deliberately left open past 8 m either side of the
    /// axis on light bands: cover that spans the field turns a driving game into
    /// a maze.
    /// </summary>
    void Reseed(Transform band, int index)
    {
        var old = band.Find("Scatter");
        if (old != null)
            DestroyImmediate(old.gameObject);

        var scatter = new GameObject("Scatter").transform;
        scatter.SetParent(band, false);

        // The opening stretch is clear — a mode that starts the player inside a
        // rock cluster reads as a bug on the first frame anyone ever sees.
        if (index < 2)
            return;

        var roll = new System.Random(index * 7919 + 13);
        var rock = ArenaMaterials.Lit("tank-rock", RockGrey, 0.18f);
        var crystal = ArenaMaterials.Emissive("tank-crystal", CrystalTeal, 1.5f);

        int blocks = 2 + roll.Next(0, 4);
        for (int i = 0; i < blocks; i++)
        {
            float x = (float)(roll.NextDouble() * 2.0 - 1.0) * (HalfWidth - 3f);
            float z = (float)roll.NextDouble() * BandLength;
            float w = 1.8f + (float)roll.NextDouble() * 3.4f;
            float h = 1.2f + (float)roll.NextDouble() * 2.6f;
            float d = 1.8f + (float)roll.NextDouble() * 3.4f;
            var block = Box(scatter, "Rock", new Vector3(x, h * 0.5f, z),
                new Vector3(w, h, d), rock, collide: true);
            block.localRotation = Quaternion.Euler(0f, (float)roll.NextDouble() * 90f, 0f);
        }

        // One crystal spire every few bands: a landmark, and the only thing on
        // the field that glows teal, so distance reads at a glance.
        if (roll.Next(0, 3) == 0)
        {
            float x = (float)(roll.NextDouble() * 2.0 - 1.0) * (HalfWidth - 4f);
            float z = (float)roll.NextDouble() * BandLength;
            var spire = Box(scatter, "Crystal", new Vector3(x, 2.2f, z),
                new Vector3(1.1f, 4.4f, 1.1f), crystal, collide: true);
            spire.localRotation = Quaternion.Euler(10f, (float)roll.NextDouble() * 90f, 6f);
        }
    }

    static Transform Box(Transform parent, string name, Vector3 position, Vector3 size,
        Material material, bool collide)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collide)
        {
            var collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go.transform;
    }

    void Sun(string name, Vector3 euler, float intensity, Color color, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(euler);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }
}
