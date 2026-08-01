using UnityEngine;

/// <summary>
/// The road that never ends.
///
/// A fixed ring of segments, recycled: when the one furthest behind the runner
/// falls out of sight it is moved to the front of the queue rather than
/// destroyed and rebuilt. So the road is infinite and the object count is
/// constant, which is the whole trick — a runner that instantiates as it goes
/// spends its frame budget on garbage collection somewhere around the third
/// minute.
///
/// Nothing here carries a collider. The fighters' feet ask BrawlGround for the
/// surface height, and BrawlGround answers 0 when no collider is found, which
/// IS the road's surface — so the colliders would only ever have confirmed the
/// default answer, while giving the walk's wall-probes something to catch on.
/// </summary>
public class ChineseRunRoad : MonoBehaviour
{
    /// <summary>Half-width of the running surface. Also the fence the fighters clamp to.</summary>
    public const float HalfWidth = 7f;

    const float SegmentLength = 24f;
    /// <summary>Enough to cover the view ahead plus what is still visible behind.</summary>
    const int SegmentCount = 16;
    /// <summary>How far behind the runner a segment may fall before it is recycled.</summary>
    const float BehindLimit = 70f;

    static readonly Color EdgeCyan = new Color(0.2f, 0.9f, 1f);
    static readonly Color DeckDark = new Color(0.09f, 0.12f, 0.17f);

    Transform[] _segments;
    float _frontEdge;

    public static ChineseRunRoad Build(Transform parent)
    {
        var go = new GameObject("Road");
        go.transform.SetParent(parent, false);
        var road = go.AddComponent<ChineseRunRoad>();
        road.Compose();
        return road;
    }

    void Compose()
    {
        // The two lights are the road's own. The arena's key light points
        // wherever the arena wanted it, and this mode never sees the arena.
        Sun("KeyLight", new Vector3(38f, 20f, 0f), 1.15f, new Color(1f, 0.97f, 0.92f), true);
        Sun("FillLight", new Vector3(28f, 200f, 0f), 0.35f, new Color(0.55f, 0.75f, 1f), false);

        _segments = new Transform[SegmentCount];
        // The first segment starts behind the runner so there is road under
        // the very first frame and road to be thrown back onto.
        _frontEdge = -SegmentLength * 3f;
        for (int i = 0; i < SegmentCount; i++)
        {
            _segments[i] = BuildSegment(i);
            _segments[i].position = new Vector3(0f, 0f, _frontEdge);
            _frontEdge += SegmentLength;
        }
    }

    /// <summary>
    /// Recycle whatever has fallen behind. Called with the runner's z every
    /// frame; cheap enough to be unconditional, since it is a compare per
    /// segment and a move for at most one or two of them.
    /// </summary>
    public void FollowRunner(float runnerZ)
    {
        foreach (var segment in _segments)
        {
            if (segment == null)
                continue;
            while (segment.position.z + SegmentLength < runnerZ - BehindLimit)
            {
                segment.position = new Vector3(0f, 0f, _frontEdge);
                _frontEdge += SegmentLength;
            }
        }
    }

    /// <summary>
    /// One tile of road: the deck, its glowing edges, a dashed centre line and
    /// a pair of pylons. Every segment is identical, so recycling one to the
    /// front is invisible — the alternative, varying them, means the seam
    /// where the variation repeats becomes the thing the eye tracks instead.
    /// </summary>
    Transform BuildSegment(int index)
    {
        var root = new GameObject($"Segment{index}").transform;
        root.SetParent(transform, false);

        var deck = ArenaMaterials.Surface("run-deck", DeckDark, EdgeCyan, 4f, 0.5f);
        var rail = ArenaMaterials.Emissive("run-rail", EdgeCyan, 2.2f);
        var dash = ArenaMaterials.Emissive("run-dash", new Color(0.6f, 0.85f, 1f), 1.5f);
        var pylon = ArenaMaterials.Lit("run-pylon", new Color(0.06f, 0.08f, 0.11f), 0.4f);
        var cap = ArenaMaterials.Emissive("run-pylon-cap", EdgeCyan, 2.0f);

        // Top surface at exactly y = 0, which is what BrawlGround answers.
        Box(root, "Deck", new Vector3(0f, -0.4f, SegmentLength * 0.5f),
            new Vector3(HalfWidth * 2f, 0.8f, SegmentLength), deck);

        foreach (float side in new[] { -1f, 1f })
        {
            Box(root, "Rail", new Vector3(side * HalfWidth, 0.03f, SegmentLength * 0.5f),
                new Vector3(0.14f, 0.06f, SegmentLength), rail);
            Box(root, "Pylon", new Vector3(side * (HalfWidth + 1.6f), 1.4f, SegmentLength * 0.5f),
                new Vector3(0.3f, 2.8f, 0.3f), pylon);
            Box(root, "PylonCap", new Vector3(side * (HalfWidth + 1.6f), 2.9f, SegmentLength * 0.5f),
                new Vector3(0.42f, 0.18f, 0.42f), cap);
        }

        // Lane dashes down the middle of each of the four lanes, so speed is
        // visible. Without them a flat road reads as standing still.
        foreach (float lane in ChineseRun.Lanes)
            for (int d = 0; d < 4; d++)
                Box(root, "Dash",
                    new Vector3(lane, 0.02f, d * (SegmentLength / 4f) + SegmentLength / 8f),
                    new Vector3(0.16f, 0.04f, 2.2f), dash);

        return root;
    }

    static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        // See the class note: the road is scenery, and a collider here is a
        // wall for the fighters' horizontal probes to find.
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
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
