using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly alien rock and monolith meshes, generated at runtime.
///
/// Cover used to be `PrimitiveType.Cube` — and a scaled cube reads as a
/// prototype no matter how good the material on it is, because silhouette
/// is the first thing the eye judges. These are the same gameplay volume
/// with an honest silhouette: 60-100 triangles, hard-edged facets, no
/// texture of their own.
///
/// Three rules make the difference between "low poly" and "cheap":
///
/// 1. FLAT SHADING. Every triangle owns its vertices, so each facet
///    catches the light separately. Smooth-shading a 60-triangle rock
///    just makes it look like a deflated ball.
/// 2. NO SYMMETRY. Each ring is rotated and pushed off-centre a little,
///    so no two sides match and the thing never reads as a barrel.
/// 3. A FLAT TOP. Robots stand and fight on cover, so the top stays a
///    level cap rather than a picturesque point — except on shards,
///    which are meant to be unstandable.
///
/// Meshes are UNIT sized (bounds -0.5..0.5) so the caller keeps scaling
/// them by the transform exactly as it scaled the cube, and every
/// collider, NavMeshObstacle and ArenaBlock animation still behaves.
/// </summary>
public static class ArenaRock
{
    public enum Form
    {
        /// <summary>Weathered rounded rock — bulges at the waist.</summary>
        Boulder,
        /// <summary>Standing slab, near vertical, flat top. Reads built.</summary>
        Monolith,
        /// <summary>Tapered spike. Unstandable on purpose.</summary>
        Shard,
        /// <summary>Eroded strata — stepped, wide-footed.</summary>
        Buttress,
    }

    static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

    /// <summary>A unit-sized rock. Same (form, seed) returns the same mesh.</summary>
    public static Mesh Unit(Form form, int seed)
    {
        int key = (int)form * 1000 + (seed & 0x3FF);
        if (Cache.TryGetValue(key, out var cached) && cached != null)
            return cached;
        var mesh = Build(form, seed);
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Drop the cached meshes on arena teardown. Generated meshes are real
    /// engine objects: letting the dictionary go without destroying them
    /// leaks one set per arena load, and this game reloads arenas all
    /// evening.
    /// </summary>
    public static void Clear()
    {
        foreach (var mesh in Cache.Values)
            if (mesh != null)
                Object.Destroy(mesh);
        Cache.Clear();
    }

    static Mesh Build(Form form, int seed)
    {
        var rng = new System.Random(seed);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        int sides = form == Form.Monolith ? rng.Next(5, 7) : rng.Next(6, 9);
        int rings = form == Form.Buttress ? 5 : 4;
        bool pointed = form == Form.Shard;

        // Ring centres wander off the axis: a rock that grew, not a lathe.
        var centres = new Vector3[rings + 1];
        Vector3 drift = new Vector3(Next(-0.06f, 0.06f), 0f, Next(-0.06f, 0.06f));
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            centres[r] = new Vector3(drift.x * t, -0.5f + t, drift.z * t);
        }

        // Radius per ring, plus a per-vertex wobble so no facet matches.
        var radii = new float[rings + 1][];
        var twist = new float[rings + 1];
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            float profile = Profile(form, t);
            twist[r] = Next(-0.35f, 0.35f);       // breaks vertical seams
            radii[r] = new float[sides];
            for (int s = 0; s < sides; s++)
                radii[r][s] = profile * Next(0.86f, 1.14f);
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        // The axis midpoint: every form here is star-shaped about it, so
        // "outward" is unambiguous for any facet.
        Vector3 shapeCentre = new Vector3(drift.x * 0.5f, 0f, drift.z * 0.5f);

        Vector3 At(int r, int s)
        {
            float angle = (s / (float)sides) * Mathf.PI * 2f + twist[r];
            float radius = radii[r][s % sides];
            return centres[r] + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        // Every triangle carries its own vertices — that is the flat shading.
        // Each one also ORIENTS ITSELF: a hand-derived winding rule is one
        // sign error away from a mesh that renders inside-out (which is
        // exactly what shipped), while "the normal must point away from the
        // axis" cannot be got backwards.
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 centroid = (a + b + c) / 3f;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), centroid - shapeCentre) < 0f)
                (b, c) = (c, b);

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = At(r, s), b = At(r, s + 1);
                Vector3 c = At(r + 1, s), d = At(r + 1, s + 1);
                if (pointed && r == rings - 1)
                {
                    Tri(a, b, centres[rings]);   // converge on the tip
                    continue;
                }
                Tri(a, b, d);
                Tri(a, d, c);
            }

        // Caps: a fan on the flat top, always one underneath so a block
        // tipped by an explosion is not hollow.
        if (!pointed)
            for (int s = 0; s < sides; s++)
                Tri(At(rings, s + 1), At(rings, s), centres[rings]);
        for (int s = 0; s < sides; s++)
            Tri(At(0, s), At(0, s + 1), centres[0]);

        var mesh = new Mesh { name = $"Rock_{form}_{seed}" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();     // faceted, because nothing is shared
        mesh.RecalculateBounds();
        FitToUnit(mesh);
        return mesh;
    }

    /// <summary>Half-width at height t — the silhouette, in one line each.</summary>
    static float Profile(Form form, float t)
    {
        switch (form)
        {
            case Form.Boulder:
                // Widest at the waist, tucked top and bottom.
                return 0.5f * (0.70f + 0.30f * Mathf.Sin(Mathf.PI * (0.15f + t * 0.8f)));
            case Form.Monolith:
                // Barely tapered: a standing slab, flat on top.
                return 0.5f * Mathf.Lerp(1f, 0.82f, t);
            case Form.Shard:
                return 0.5f * Mathf.Pow(1f - t, 0.75f);
            default:
                // Stepped strata — a broad foot, a shoulder, a narrow crown.
                if (t < 0.35f) return 0.5f * Mathf.Lerp(1f, 0.86f, t / 0.35f);
                if (t < 0.45f) return 0.5f * 0.66f;
                return 0.5f * Mathf.Lerp(0.62f, 0.5f, (t - 0.45f) / 0.55f);
        }
    }

    /// <summary>
    /// Rescale into the unit box so the caller's transform.localScale means
    /// exactly what it meant for the cube — the whole reason colliders and
    /// block animations need no changes.
    /// </summary>
    static void FitToUnit(Mesh mesh)
    {
        var bounds = mesh.bounds;
        var scale = new Vector3(
            bounds.size.x > 1e-4f ? 1f / bounds.size.x : 1f,
            bounds.size.y > 1e-4f ? 1f / bounds.size.y : 1f,
            bounds.size.z > 1e-4f ? 1f / bounds.size.z : 1f);

        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i] - bounds.center;
            verts[i] = new Vector3(v.x * scale.x, v.y * scale.y, v.z * scale.z);
        }
        mesh.SetVertices(verts);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Which rock an arena's surface style calls for, so the roster keeps
    /// the variety it has: crystal fields spike, quarries erode, hull-plated
    /// stations get built slabs.
    /// </summary>
    public static Form FormFor(ArenaMaterials.SurfaceStyle style, int index)
    {
        switch (style)
        {
            case ArenaMaterials.SurfaceStyle.Crystal:
                return index % 3 == 0 ? Form.Monolith : Form.Shard;
            case ArenaMaterials.SurfaceStyle.Organic:
                return index % 4 == 0 ? Form.Shard : Form.Boulder;
            case ArenaMaterials.SurfaceStyle.Stone:
            case ArenaMaterials.SurfaceStyle.Strata:
                return index % 3 == 0 ? Form.Buttress : Form.Boulder;
            default:
                // Built surfaces — plating, brick, tread — want built shapes.
                return index % 3 == 0 ? Form.Buttress : Form.Monolith;
        }
    }
}
