using UnityEngine;

/// <summary>
/// The Lift Pad: a hover platform cycling between the deck and the high
/// line. It IS terrain — a fighter standing on it rides it up and down
/// through the ordinary FollowGround rule, no special cases anywhere.
/// </summary>
public class BrawlLift : MonoBehaviour
{
    const float Width = 2.2f;
    const float Travel = 2.1f;
    const float Period = 9f;

    float _phase;
    float _top;

    public static void Spawn(Transform stageRoot, float x, float phase)
    {
        var go = new GameObject("BrawlLift");
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(x, 0f, BrawlStage.SpawnZ);

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Pad";
        // The collider STAYS: physics crates land on lifts, and the
        // terrain probe reads the pad directly.
        pad.transform.SetParent(go.transform, false);
        pad.transform.localScale = new Vector3(Width, 0.22f, 2.4f);
        // Plain lit, not the world-space Surface — a rising pad slid
        // through its own pattern (same defect the crates had).
        pad.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Lit("brawl-lift-pad", new Color(0.12f, 0.17f, 0.24f), 0.45f);

        var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        glow.name = "Underglow";
        Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(go.transform, false);
        glow.transform.localPosition = new Vector3(0f, -0.16f, 0f);
        glow.transform.localScale = new Vector3(Width * 0.8f, 0.06f, 2.0f);
        glow.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("brawl-lift-glow", new Color(0.2f, 0.9f, 1f), 2.0f);

        var lift = go.AddComponent<BrawlLift>();
        lift._phase = phase;
        BrawlGround.Register(lift, lift.WalkableTop);
    }

    void Update()
    {
        // Sits at the deck for a beat of the cycle, so boarding is easy.
        float wave = Mathf.Sin((Time.time / Period + _phase) * 2f * Mathf.PI);
        float height = Mathf.Max(0f, wave) * Travel;
        var p = transform.localPosition;
        transform.localPosition = new Vector3(p.x, height, p.z);
        _top = transform.position.y + 0.11f;
    }

    float WalkableTop(float x, float z)
    {
        if (Mathf.Abs(x - transform.position.x) > Width * 0.5f
            || Mathf.Abs(z - transform.position.z) > 1.2f)
            return float.NaN;
        return _top;
    }

    void OnDestroy()
    {
        BrawlGround.Unregister(this);
    }
}
