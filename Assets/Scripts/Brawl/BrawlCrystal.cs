using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The brawl stages' crystal growths.
///
/// These used to be a scaled <c>PrimitiveType.Cube</c> wearing a flat
/// emissive material, which is exactly what they looked like: a blue box.
/// A crystal needs three things a box cannot fake —
///
/// 1. A POINT. Six sides tapering to a tip is the whole silhouette. A slab
///    rotated 45 degrees still reads as a slab.
/// 2. FACETS. Flat-shaded faces catching the light at different angles.
///    Emissive-only materials have no shading at all, so a lit crystal and
///    an unlit one look identical — which is why the old ones read as
///    untextured. These use the same faceted PhotonArena/Surface style the
///    FPS Crystal Hollow arena grows its spires from.
/// 3. CLUSTERS. Crystals do not grow one to a spot. A tall shard with
///    smaller ones leaning off its base reads as mineral; a lone spike
///    reads as a traffic cone.
/// </summary>
public static class BrawlCrystal
{
    const int Sides = 6;

    // Base, shoulder, neck — then a point. The waist bulge is what keeps the
    // profile from reading as a pencil.
    static readonly float[] RingY = { 0f, 0.44f, 0.66f };
    static readonly float[] RingR = { 0.38f, 0.50f, 0.26f };

    static Mesh _shard;

    /// <summary>
    /// A hexagonal shard: base at y = 0, tip at y = 1, radius ~0.5. Flat
    /// shaded — every face carries its own vertices and its own normal, so
    /// the facets stay hard instead of smearing into a smooth cone.
    /// PhotonArena/Surface is triplanar, so no UVs are needed.
    /// </summary>
    public static Mesh ShardMesh
    {
        get
        {
            if (_shard != null)
                return _shard;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            void Face(Vector3 outward, params Vector3[] p)
            {
                var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                bool flip = Vector3.Dot(n, outward) < 0f;
                if (flip)
                    n = -n;
                int start = verts.Count;
                for (int i = 0; i < p.Length; i++)
                {
                    verts.Add(p[i]);
                    norms.Add(n);
                }
                for (int i = 1; i < p.Length - 1; i++)
                {
                    tris.Add(start);
                    tris.Add(start + (flip ? i + 1 : i));
                    tris.Add(start + (flip ? i : i + 1));
                }
            }

            Vector3 Ring(int ring, int side)
            {
                float a = (side % Sides) / (float)Sides * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * RingR[ring], RingY[ring], Mathf.Sin(a) * RingR[ring]);
            }

            var tip = new Vector3(0f, 1f, 0f);
            int top = RingY.Length - 1;
            for (int s = 0; s < Sides; s++)
            {
                Vector3 sideOut = (Ring(1, s) + Ring(1, s + 1)) * 0.5f;
                sideOut.y = 0f;
                for (int r = 0; r < top; r++)
                    Face(sideOut, Ring(r, s), Ring(r + 1, s), Ring(r + 1, s + 1), Ring(r, s + 1));
                Face(sideOut + Vector3.up * 0.6f, Ring(top, s), tip, Ring(top, s + 1));
                // Base cap, so a shard seen from below is not hollow.
                Face(Vector3.down, Vector3.zero, Ring(0, s), Ring(0, s + 1));
            }

            _shard = new Mesh { name = "BrawlCrystalShard" };
            _shard.SetVertices(verts);
            _shard.SetNormals(norms);
            _shard.SetTriangles(tris, 0);
            _shard.RecalculateBounds();
            return _shard;
        }
    }

    /// <summary>The faceted, faintly glowing quarry crystal.</summary>
    public static Material Material =>
        ArenaMaterials.Style("brawl-crystal", ArenaMaterials.SurfaceStyle.Crystal,
                             new Color(0.17f, 0.44f, 0.60f), new Color(0.06f, 0.19f, 0.32f),
                             0.8f, roughness: 0.16f,
                             emit: new Color(0.45f, 0.9f, 1f), emitStrength: 1.5f,
                             bump: 0.8f, cavity: 0.35f);

    /// <summary>The dark rock a cluster erupts from.</summary>
    public static Material RockMaterial =>
        ArenaMaterials.Style("brawl-quarry-rock", ArenaMaterials.SurfaceStyle.Stone,
                             new Color(0.13f, 0.14f, 0.19f), new Color(0.05f, 0.05f, 0.08f),
                             2.4f, roughness: 0.96f, bump: 1.5f, cavity: 0.7f);

    /// <summary>
    /// Grow a cluster: one tall shard, two or three lesser ones leaning off
    /// its base, and a low rock collar hiding where they meet the floor.
    /// </summary>
    public static GameObject Build(Transform parent, Vector3 basePos, float scale, bool light = false)
    {
        var cluster = new GameObject("Crystal");
        cluster.transform.SetParent(parent, false);
        cluster.transform.localPosition = basePos;

        var mat = Material;
        Shard(cluster.transform, Vector3.zero, scale * 1.5f, scale * 0.62f,
              Random.Range(-9f, 9f), Random.Range(0f, 360f), mat);

        int lesser = Random.Range(2, 4);
        for (int i = 0; i < lesser; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = scale * Random.Range(0.28f, 0.5f);
            Shard(cluster.transform,
                  new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                  scale * Random.Range(0.45f, 0.85f), scale * Random.Range(0.28f, 0.42f),
                  Random.Range(14f, 30f), a * Mathf.Rad2Deg + Random.Range(-40f, 40f), mat);
        }

        // Rock collar: the seam between crystal and floor is the tell that
        // these are props sitting on a plane. Bury it.
        var collar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(collar.GetComponent<Collider>());
        collar.name = "Collar";
        collar.transform.SetParent(cluster.transform, false);
        collar.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        collar.transform.localScale = new Vector3(scale * 1.25f, 0.16f, scale * 1.25f);
        collar.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
        collar.GetComponent<MeshRenderer>().sharedMaterial = RockMaterial;

        if (light)
        {
            var glow = new GameObject("CrystalGlow").AddComponent<Light>();
            glow.transform.SetParent(cluster.transform, false);
            glow.transform.localPosition = new Vector3(0f, scale * 1.1f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(0.45f, 0.9f, 1f);
            glow.intensity = 1.4f;
            glow.range = scale * 6f;
        }
        return cluster;
    }

    static void Shard(Transform parent, Vector3 offset, float height, float width,
                      float lean, float spin, Material mat)
    {
        var go = new GameObject("Shard");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.Euler(0f, spin, 0f) * Quaternion.Euler(lean, 0f, 0f);
        go.transform.localScale = new Vector3(width, height, width);
        go.AddComponent<MeshFilter>().sharedMesh = ShardMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
