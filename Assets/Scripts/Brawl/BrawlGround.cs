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
        public System.Func<float, float> topAt;   // NaN = not under this x
    }

    static readonly List<Platform> Platforms = new List<Platform>();

    public static void Clear() => Platforms.Clear();

    /// <summary>Register a platform: return the surface height at x, or NaN when x is off it.</summary>
    public static void Register(Component owner, System.Func<float, float> topAt)
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
    /// </summary>
    public static float HeightAt(float x, float below = float.PositiveInfinity, Component exclude = null)
    {
        float best = 0f;
        // DefaultRaycastLayers skips Ignore Raycast — where fighters'
        // bumper capsules and still-bouncing crates live.
        if (Physics.Raycast(new Vector3(x, ProbeTop, 0f), Vector3.down, out var hit,
                ProbeTop + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            float top = hit.point.y;
            if (top > best && top <= below + 0.3f)
                best = top;
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
            float top = platform.topAt(x);
            if (float.IsNaN(top) || top > below + 0.3f)
                continue;
            if (top > best)
                best = top;
        }
        return best;
    }
}
