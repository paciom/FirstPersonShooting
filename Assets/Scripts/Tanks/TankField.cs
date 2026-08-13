using System.Collections.Generic;
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
    /// Hold a pawn inside the fence — and, for the HERO ONLY, behind the
    /// frontier.
    ///
    /// The back edge is what turns "drive wherever" into a scrolling game, and
    /// it applies to the one pawn the screen follows. A pawn parked against it
    /// is being pushed up the field at <see cref="ScrollSpeed"/> — slowly
    /// enough to be a nudge rather than a shove, but it never stops.
    ///
    /// Everything else must be free to fall PAST that line, because the sweep
    /// (<see cref="FallenBehind"/>) collects raiders well behind it — a raider
    /// held at the trail line can never reach the sweep, and every tank slower
    /// than the hero ends up pinned there in a jostling scrum at the bottom of
    /// the screen.
    /// </summary>
    public static Vector3 Clamp(Vector3 position, float radius, bool heldAtTrail)
    {
        float lane = Mathf.Max(1f, HalfWidth - radius);
        position.x = Mathf.Clamp(position.x, -lane, lane);
        if (heldAtTrail)
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
        // OBJECT-SPACE patterns, all three: every block here is pushable (see
        // TankBlock), and a world-space pattern on a pushed block stays put in
        // the world while the mesh slides through it — which reads as the
        // texture changing under the player's bumper. The -os keys keep these
        // apart from any world-space material another mode cached this session.
        var rock = ArenaMaterials.Style("tank-rock-os", ArenaMaterials.SurfaceStyle.Stone,
            RockGrey, RockGrey * 0.45f, 1.6f, 0.9f, objectSpace: true);
        var brick = ArenaMaterials.Style("tank-brick-os", ArenaMaterials.SurfaceStyle.Brick,
            new Color(0.42f, 0.20f, 0.14f), new Color(0.58f, 0.42f, 0.30f), 0.6f, 0.85f,
            objectSpace: true);
        var wood = ArenaMaterials.Style("tank-wood-os", ArenaMaterials.SurfaceStyle.Plank,
            new Color(0.52f, 0.37f, 0.20f), new Color(0.28f, 0.18f, 0.09f), 0.4f, 0.75f,
            objectSpace: true);

        // Ground footprints already claimed on this band: x, z, circle radius.
        // Checked before anything is planted, so no two pieces of scenery can
        // seed fused into each other.
        var claimed = new List<Vector3>();
        bool Claim(float radius, float halfLane, out float x, out float z)
        {
            // z stays a whole footprint inside the band, which also keeps this
            // band's furniture off whatever the NEIGHBOURING band planted at
            // its own edge — the one overlap this list cannot see.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                x = (float)(roll.NextDouble() * 2.0 - 1.0) * halfLane;
                z = radius + (float)roll.NextDouble() * (BandLength - radius * 2f);
                bool fits = true;
                foreach (var spot in claimed)
                    if ((new Vector2(x, z) - new Vector2(spot.x, spot.y)).sqrMagnitude
                        < (radius + spot.z) * (radius + spot.z))
                    {
                        fits = false;
                        break;
                    }
                if (!fits)
                    continue;
                claimed.Add(new Vector3(x, z, radius));
                return true;
            }
            // A crowded band plants one piece fewer rather than two fused ones.
            x = z = 0f;
            return false;
        }

        // Street furniture in three materials, all pushable (see TankBlock):
        // stone the anchor cover, brick the cover shells re-landscape, wood
        // the crates a hull just drives through. Kind is part of the band's
        // seeded identity like everything else here.
        int blocks = 2 + roll.Next(0, 4);
        for (int i = 0; i < blocks; i++)
        {
            int kindRoll = roll.Next(0, 10);
            TankBlockKind kind = kindRoll < 4 ? TankBlockKind.Rock
                : kindRoll < 7 ? TankBlockKind.Brick : TankBlockKind.Wood;

            Transform block;
            if (kind == TankBlockKind.Wood)
            {
                // Crates: small, near-cubic, factory-square — yaw stays shy.
                float side = 1.2f + (float)roll.NextDouble() * 1f;
                if (!Claim(side * 0.71f, HalfWidth - 3f, out float x, out float z))
                    continue;
                block = Box(scatter, "Wood", new Vector3(x, side * 0.5f, z),
                    new Vector3(side, side, side), wood, collide: true);
                block.localRotation = Quaternion.Euler(0f, (float)roll.NextDouble() * 18f, 0f);
            }
            else
            {
                float w = 1.8f + (float)roll.NextDouble() * (kind == TankBlockKind.Brick ? 2.2f : 3.4f);
                float h = 1.2f + (float)roll.NextDouble() * 2.2f;
                float d = 1.8f + (float)roll.NextDouble() * (kind == TankBlockKind.Brick ? 2.2f : 3.4f);
                // Half the ground diagonal: the circle a yawed box always fits in.
                if (!Claim(Mathf.Sqrt(w * w + d * d) * 0.5f, HalfWidth - 3f,
                        out float x, out float z))
                    continue;
                block = Box(scatter, kind.ToString(), new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), kind == TankBlockKind.Brick ? brick : rock,
                    collide: true);
                block.localRotation = Quaternion.Euler(0f,
                    (float)roll.NextDouble() * (kind == TankBlockKind.Brick ? 20f : 90f), 0f);
            }
            block.gameObject.AddComponent<TankBlock>().Configure(kind);
        }

        // One crystal cluster every few bands: a landmark, and the only thing
        // on the field that glows teal, so distance reads at a glance. Grown
        // by BrawlCrystal — real faceted shards with a rock collar — because
        // the old flat emissive box read as a missing texture. Its shape
        // rolls UnityEngine.Random, so the state is pinned to the band index
        // and restored: band N grows the same cluster every time it comes
        // round, and nobody else's random draw is disturbed.
        if (roll.Next(0, 3) == 0)
        {
            float size = 2.2f + (float)roll.NextDouble() * 0.8f;
            if (Claim(size * 0.8f, HalfWidth - 4f, out float sx, out float sz))
            {
                var state = Random.state;
                Random.InitState(index * 131 + 7);
                var cluster = BrawlCrystal.Build(scatter, new Vector3(sx, 0f, sz), size,
                    light: true);
                Random.state = state;
                // The cluster is COVER, so it needs the collider the brawl
                // prop never carried: bolts stop on it and hulls part around
                // it, boxed to the tall central shard.
                var box = cluster.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size * 0.75f, 0f);
                box.size = new Vector3(size * 0.95f, size * 1.5f, size * 0.95f);
            }
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
