using UnityEngine;

/// <summary>
/// One combatant on the Brawl lane. This phase: spawn on the stage, wear the
/// picked robot, face the opponent. The combat state machine lands in the
/// combat-core phase — this class is its skeleton on purpose.
/// </summary>
public class BrawlFighter : MonoBehaviour
{
    public int TeamId { get; private set; }
    public BrawlFighter Opponent { get; set; }

    /// <summary>+1 facing right (toward +X), -1 facing left.</summary>
    public float Facing { get; private set; } = 1f;

    Transform _body;
    Animator _animator;

    public static BrawlFighter Spawn(Transform stageRoot, RobotRoster roster,
        int robotIndex, int teamId)
    {
        var go = new GameObject(teamId == 0 ? "BrawlFighter_P1" : "BrawlFighter_P2");
        go.transform.SetParent(stageRoot, false);
        // Cyan opens on the left, the classic P1 side.
        float side = teamId == 0 ? -1f : 1f;
        go.transform.localPosition = new Vector3(side * BrawlStage.StartOffset, 0f, 0f);

        var fighter = go.AddComponent<BrawlFighter>();
        fighter.TeamId = teamId;

        // Root -> Body -> Model, the shape every character in the project
        // has; effects and factories all expect a "Body" to hang off.
        var body = new GameObject("Body").transform;
        body.SetParent(go.transform, false);
        body.localPosition = new Vector3(0f, 1.0f, 0f);
        fighter._body = body;

        Color tint = MatchAnnouncer.TeamColor(teamId);
        var entry = (roster != null && roster.HasRobots)
            ? roster.Get(robotIndex)
            : default(RobotRoster.Entry);
        if (entry.modelPrefab != null)
        {
            var model = RobotFactory.InstantiateNormalized(entry.modelPrefab, body, tint);
            fighter._animator = model.GetComponentInChildren<Animator>(true);

            // RobotLocomotion sniffs speed from position deltas of whatever it
            // decides the character root is — under the stage hierarchy that
            // is the wrong transform entirely. The fighter knows its own
            // velocity exactly and will drive the Speed float itself.
            var locomotion = model.GetComponentInChildren<RobotLocomotion>(true);
            if (locomotion != null)
                locomotion.enabled = false;
        }
        else
        {
            // No roster (scene built before any robots were downloaded):
            // a tinted capsule keeps the mode testable.
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Model";
            capsule.transform.SetParent(body, false);
            Object.Destroy(capsule.GetComponent<Collider>());
            capsule.GetComponent<MeshRenderer>().sharedMaterial =
                ArenaMaterials.Lit($"brawl-fallback-{teamId}", tint, 0.5f);
        }

        return fighter;
    }

    void Update()
    {
        FaceOpponent();
    }

    /// <summary>
    /// Robots model +Z as forward and the lane runs along X, so facing the
    /// opponent is a ±90° yaw. Continuous — fighters that cross mid-air swap
    /// sides the moment they land the crossing.
    /// </summary>
    void FaceOpponent()
    {
        if (Opponent == null)
            return;
        Facing = Opponent.transform.position.x >= transform.position.x ? 1f : -1f;
        transform.rotation = Quaternion.Euler(0f, 90f * Facing, 0f);
    }
}
