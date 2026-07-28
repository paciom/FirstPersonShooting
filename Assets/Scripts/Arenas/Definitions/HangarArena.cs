using UnityEngine;

/// <summary>
/// The original ship-deck arena. Uniquely among the set it is authored into the
/// scene by ArenaBuilder rather than generated, so loading it re-activates that
/// geometry instead of building anything — which is also what lets the arena
/// system ship without re-running the scene builder.
/// </summary>
public class HangarArena : ArenaDefinition
{
    public override string DisplayName => "HANGAR";
    public override string Tagline => "Neon ship deck. Cover that moves, breaks and grows back.";
    public override bool SceneAuthored => true;

    public override ArenaPalette Palette => ArenaPalette.Default;

    public override Vector2 HalfExtent => new Vector2(16f, 16f);
    public override float CoverHalfExtent => 14f;

    public override Vector3 PlayerSpawn => new Vector3(0f, 0.1f, -15f);

    public override Vector3[] TeamSpawns(int teamId)
    {
        return teamId == 0
            ? new[] { new Vector3(-8f, 0f, -15f), new Vector3(-4f, 0f, -16f), new Vector3(8f, 0f, -15f) }
            : new[] { new Vector3(-8f, 0f, 15f), new Vector3(0f, 0f, 16f), new Vector3(8f, 0f, 15f) };
    }

    public override Vector3[] KeepOut
    {
        get
        {
            var spawns = base.KeepOut;
            // Plus the two spawn portals and the four crates ArenaBuilder places.
            var props = new[]
            {
                new Vector3(0f, 0f, -18.2f), new Vector3(0f, 0f, 18.2f),
                new Vector3(10f, 0f, -12f), new Vector3(-7f, 0f, -13f),
                new Vector3(16.5f, 0f, 8f), new Vector3(-16.5f, 0f, -8f),
            };
            var all = new Vector3[spawns.Length + props.Length];
            spawns.CopyTo(all, 0);
            props.CopyTo(all, spawns.Length);
            return all;
        }
    }

    /// <summary>Nothing to build — ArenaRuntime re-activates the scene geometry.</summary>
    public override void Build(Transform root, ArenaKit kit) { }
}
