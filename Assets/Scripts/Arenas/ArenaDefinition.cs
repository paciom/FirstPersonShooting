using UnityEngine;

/// <summary>
/// One arena: its identity, its bounds, where characters start, and how to
/// build it. Plain C# — definitions are data plus a Build method, not scene
/// objects, so the whole set can be listed by the select screen without any of
/// them existing in the world.
/// </summary>
public abstract class ArenaDefinition
{
    public abstract string DisplayName { get; }

    /// <summary>One line for the select card. Say what makes it different.</summary>
    public abstract string Tagline { get; }

    public abstract ArenaPalette Palette { get; }

    /// <summary>Playable interior half-extent, used for treasure drops.</summary>
    public virtual Vector2 HalfExtent => new Vector2(16f, 16f);

    /// <summary>Where dynamic cover blocks are allowed to churn.</summary>
    public virtual float CoverHalfExtent => 14f;

    /// <summary>The plane dynamic cover slides on. Cover stays on one level.</summary>
    public virtual float GroundY => 0f;

    /// <summary>How many destructible/regrowing cover blocks to scatter.</summary>
    public virtual int CoverCount => 24;

    /// <summary>Shown as a badge on the select card. 1 = flat.</summary>
    public virtual int Levels => 1;

    /// <summary>
    /// True only for HANGAR, which is authored into the scene by ArenaBuilder
    /// rather than generated. Loading it re-activates the scene geometry instead
    /// of running Build().
    /// </summary>
    public virtual bool SceneAuthored => false;

    // ---------- where everyone starts ----------

    public virtual Vector3 PlayerSpawn => new Vector3(0f, 0.1f, -15f);

    /// <summary>The three bot spawns for a team. Team 0 also hosts the player.</summary>
    public abstract Vector3[] TeamSpawns(int teamId);

    /// <summary>Where mid-match reinforcement robots materialize.</summary>
    public virtual Vector3 ReinforcementLine(int teamId)
    {
        return new Vector3(Random.Range(-9f, 9f), 0f, teamId == 0 ? -16f : 16f);
    }

    /// <summary>
    /// Y levels treasure drops may land on. Multi-level arenas list every floor,
    /// or the upper storeys never receive loot and the vertical space goes dead.
    /// </summary>
    public virtual float[] DropPlanes => new[] { 0f };

    public virtual Vector3 SpectatorPerch => new Vector3(0f, 8f, -14f);

    /// <summary>Keep-out points cover and loot avoid — spawns, by default.</summary>
    public virtual Vector3[] KeepOut
    {
        get
        {
            var team0 = TeamSpawns(0);
            var team1 = TeamSpawns(1);
            var all = new Vector3[team0.Length + team1.Length + 1];
            team0.CopyTo(all, 0);
            team1.CopyTo(all, team0.Length);
            all[all.Length - 1] = PlayerSpawn;
            return all;
        }
    }

    // ---------- construction ----------

    /// <summary>
    /// Lay out this arena's geometry under <paramref name="root"/>. Everything
    /// needs a collider to be walkable, and everything is collected by the
    /// NavMeshSurface bake that runs straight afterwards.
    /// </summary>
    public abstract void Build(Transform root, ArenaKit kit);

    /// <summary>
    /// Fog, ambient, and bloom. The default reads it all off the palette; an
    /// arena only overrides this if it needs something structural, like a
    /// skybox.
    /// </summary>
    public virtual void ApplyAtmosphere()
    {
        var p = Palette;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = p.ambient;
        RenderSettings.fog = p.fogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = p.fog;
        RenderSettings.fogDensity = p.fogDensity;
    }
}
