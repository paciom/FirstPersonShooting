using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The lane's terrain authority: what height the ground is at any x.
/// Flat stage = 0 everywhere; crates and lift pads register themselves as
/// platforms and the fighters' feet ask here every frame — which is the
/// whole trick that lets a robot stand on a crate or ride an elevator
/// without a physics engine appearing anywhere.
///
/// Static registry, cleared by BrawlController on Begin: recompile-during-
/// Play stops playback in this project (AutoRebuildOnPlay forces the pref),
/// so statics can't leak across a session.
/// </summary>
public static class BrawlGround
{
    class Platform
    {
        public Component owner;
        public System.Func<float, float, float> topAt;   // (x, z) => top, NaN = not here
    }

    static readonly List<Platform> Platforms = new List<Platform>();

    public static void Clear() => Platforms.Clear();

    /// <summary>Register a platform: surface height at (x, z), NaN when off it.</summary>
    public static void Register(Component owner, System.Func<float, float, float> topAt)
    {
        Platforms.Add(new Platform { owner = owner, topAt = topAt });
    }

    public static void Unregister(Component owner)
    {
        for (int i = Platforms.Count - 1; i >= 0; i--)
            if (Platforms[i].owner == owner || Platforms[i].owner == null)
                Platforms.RemoveAt(i);
    }

    // The physics probe starts just above jump apex + head height: remix
    // arenas have catwalks and arches overhead, and a probe from higher up
    // would report THEM as the ground — turning everything underneath into
    // an impassable wall.
    const float ProbeTop = 3.4f;

    /// <summary>
    /// The highest standable surface at x (the base stage floor is 0).
    /// Real scene colliders count too — remix stages leave the arena's
    /// cover blocks standing, and those are honest platforms and walls,
    /// not decoration to clip through. Hurtboxes are triggers and ignored.
    /// <paramref name="below"/> ignores anything above that height + 0.3 —
    /// a falling crate asks what it will land ON, not where its own top is;
    /// <paramref name="exclude"/> keeps it from standing on itself.
    /// <paramref name="aboveY"/> raises the probe start relative to the
    /// ASKER: a robot standing on a five-metre ziggurat tier probes from
    /// its own head height, so tall structures are real floors instead of
    /// surfaces the fixed-height probe was born inside of (and blind to).
    /// </summary>
    public static float HeightAt(float x, float z, float below = float.PositiveInfinity,
        Component exclude = null, float aboveY = float.NegativeInfinity)
    {
        float best = 0f;
        float probeStart = Mathf.Max(ProbeTop, aboveY + 2.4f);
        // DefaultRaycastLayers skips Ignore Raycast — where fighters'
        // bumper capsules and still-bouncing crates live. The fight is a
        // PLANE now, so the probe takes both coordinates. Columns (stalks,
        // pillars) are walls, never floors — the ray looks straight through
        // their tops to whatever honest ground lies beneath, else a spawn
        // or a BREAK could stand a robot on a tree trunk.
        float rayFrom = probeStart;
        for (int guard = 0; guard < 6; guard++)
        {
            if (!Physics.Raycast(new Vector3(x, rayFrom, z), Vector3.down, out var hit,
                    rayFrom + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                break;
            if (hit.collider.GetComponent<ArenaColumn>() != null)
            {
                rayFrom = hit.point.y - 0.05f;
                if (rayFrom <= 0f)
                    break;
                continue;
            }
            float top = hit.point.y;
            if (top > best && top <= below + 0.3f)
                best = top;
            break;
        }

        for (int i = Platforms.Count - 1; i >= 0; i--)
        {
            var platform = Platforms[i];
            if (platform.owner == null)
            {
                Platforms.RemoveAt(i);
                continue;
            }
            if (platform.owner == exclude)
                continue;
            float top = platform.topAt(x, z);
            if (float.IsNaN(top) || top > below + 0.3f)
                continue;
            if (top > best)
                best = top;
        }
        return best;
    }
}
