using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The seeker head on a DOGFIGHT missile. The projectile itself is a
/// <see cref="GenericBolt"/> — raycast stepping, homing turn rate, splash,
/// trail, all arena-standard — dressed in a Missiles Pack body. This
/// component adds the three things a dogfight needs on top:
///
/// LOCK. The bolt's <c>homingTarget</c> is set at launch, so the missile
/// chases the thing that was locked, not whatever shield drifts nearest.
///
/// SEDUCTION. Flares are real one-point shields (see DogfightFlare), so all
/// this head has to do is LOOK at them: while the quarry is a jet, any live
/// flare of the quarry's team inside the seeker cone gets a periodic chance
/// to steal the lock. A jet that breaks hard AND drops flares defeats the
/// missile; a jet that flies straight and prays does not. When the stolen
/// flare burns out mid-chase the bolt falls back to its scan and may
/// re-acquire — which is why you keep breaking after you flare.
///
/// FUSE. Jets are small and fast and a raycast is a line, so near misses
/// detonate by proximity instead of demanding a literal impalement.
///
/// The registry is what brains and HUDs query for "is anything chasing me" —
/// the same plain-static shape every scene-root actor in this project uses.
/// </summary>
public class DogfightMissile : MonoBehaviour
{
    public enum Flavor { Jet, Turret }

    /// <summary>Seconds between seduction checks, and the odds per check.
    /// Several checks happen across a flare's two-second burn, so the
    /// cumulative odds are high without ever being a guarantee.</summary>
    const float SeduceInterval = 0.2f;
    const float SeduceChance = 0.55f;
    const float SeduceRange = 30f;
    const float SeduceCone = 55f;

    const float FuseRadius = 2.6f;

    static readonly List<DogfightMissile> Live = new List<DogfightMissile>();

    public static IReadOnlyList<DogfightMissile> All => Live;

    public static void DespawnAll()
    {
        // Swept at teardown BEFORE the arena cast comes back: a live homing
        // bolt outliving the mode would happily chase the menu's robots.
        for (int i = Live.Count - 1; i >= 0; i--)
            if (Live[i] != null)
                Destroy(Live[i].gameObject);
        Live.Clear();
    }

    public GenericBolt Bolt { get; private set; }

    /// <summary>The root this missile is currently chasing (jet, turret or a
    /// flare that stole the lock). Null once the bolt is coasting on its scan.</summary>
    public Transform Quarry => Bolt != null ? Bolt.homingTarget : null;

    float _nextSeduce;

    void OnEnable()
    {
        if (!Live.Contains(this))
            Live.Add(this);
    }

    void OnDisable()
    {
        Live.Remove(this);
    }

    /// <summary>
    /// Fire one. Both launchers — jets and turrets — come through here so the
    /// two flavours stay tuned against each other: the turret's shot is
    /// slower and heavier-looking, the jet's is the sharper chaser, and both
    /// turn poorly enough that a committed break out-turns them.
    /// </summary>
    public static DogfightMissile Launch(Vector3 from, Vector3 direction, int teamId,
        Transform ownerRoot, Transform lockRoot, Flavor flavor)
    {
        bool jet = flavor == Flavor.Jet;
        var spec = new BoltSpec
        {
            // Jet shots outrun a full boost (46 v 40) so a stern chase ends;
            // the turret's do NOT (34 v 40) — running from artillery works,
            // by design, because the battery is pressure and not the duel.
            speed = jet ? 46f : 34f,
            damage = jet ? 30f : 22f,
            homingDegreesPerSecond = jet ? 140f : 105f,
            // The fallback scan's reach once a lock dies. Modest on purpose:
            // a missile that lost its flare well off your tail should coast
            // out, not boomerang from across the arena.
            homingRange = 45f,
            lifetime = jet ? 6f : 7.5f,
            size = 0.16f,
            glow = 1.6f,                // a metal body, not an energy bolt
            color = jet ? MatchAnnouncer.TeamColor(teamId) : new Color(1f, 0.55f, 0.1f),
            splashRadius = jet ? 3f : 2.6f,
            splashScale = 1.3f,
            trailTime = 0.5f,
            trailWidth = 0.24f,
            trailGlow = 1.7f,           // the exhaust IS the read at this speed
        };

        // War FX rides on top of the bolt's own glow splash: the glow is the
        // energy read, the pack's flame-and-smoke is the WEIGHT.
        spec.onImpact = (bolt, hit) =>
            WarFx.Spawn(WarFx.Kind.Small, hit.point + hit.normal * 0.3f, 1.2f);
        spec.onExpire = bolt =>
            WarFx.Spawn(WarFx.Kind.Small, bolt.transform.position, 0.7f);

        var spawned = GenericBolt.Spawn(from, direction.normalized, spec, teamId, ownerRoot);
        spawned.homingTarget = lockRoot;
        MissileModels.Dress(spawned, jet ? 0 : 2, jet ? 1.1f : 1.5f);

        var missile = spawned.gameObject.AddComponent<DogfightMissile>();
        missile.Bolt = spawned;
        return missile;
    }

    void Update()
    {
        if (Bolt == null)
        {
            // The bolt died this frame (impact, fuse, lifetime) or a mid-Play
            // recompile wiped it; either way there is nothing left to steer.
            Destroy(this);
            return;
        }

        TryFuse();
        if (Bolt != null && Time.time >= _nextSeduce)
        {
            _nextSeduce = Time.time + SeduceInterval;
            TrySeduce();
        }
    }

    void TryFuse()
    {
        var quarry = Quarry;
        if (quarry == null)
            return;
        var shield = quarry.GetComponent<EnergyShield>();
        if (shield == null || shield.IsDown)
            return;
        if ((WeaponUtil.Center(shield) - transform.position).sqrMagnitude
            <= FuseRadius * FuseRadius)
        {
            WarFx.Spawn(WarFx.Kind.Small, transform.position, 1.2f);
            Bolt.Die(transform.position);
        }
    }

    /// <summary>One look at the sky for a better (worse) idea. Only jets can
    /// be flared off — a turret is a building, and buildings do not burn
    /// magnesium.</summary>
    void TrySeduce()
    {
        var quarry = Quarry;
        if (quarry == null || quarry.GetComponent<JetPawn>() == null)
            return;
        int quarryTeam = quarry.GetComponent<JetPawn>().Team;

        DogfightFlare best = null;
        float bestSqr = SeduceRange * SeduceRange;
        Vector3 heading = Bolt.Velocity.sqrMagnitude > 0.01f
            ? Bolt.Velocity.normalized
            : transform.forward;
        foreach (var flare in DogfightFlare.All)
        {
            if (flare == null || flare.Team != quarryTeam)
                continue;
            Vector3 to = flare.transform.position - transform.position;
            float sqr = to.sqrMagnitude;
            if (sqr > bestSqr || Vector3.Angle(heading, to) > SeduceCone)
                continue;
            bestSqr = sqr;
            best = flare;
        }

        if (best != null && Random.value < SeduceChance)
            Bolt.homingTarget = best.transform;
    }
}
