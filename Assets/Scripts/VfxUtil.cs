using UnityEngine;

/// <summary>
/// Procedural particle bursts so the greybox phase has juicy, camera-readable
/// feedback without any imported VFX assets. Replaced by authored VFX Graph
/// effects in the polish phase.
/// </summary>
public static class VfxUtil
{
    static Material _particleMaterial;

    static Material ParticleMaterial
    {
        get
        {
            if (_particleMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                _particleMaterial = new Material(shader);
            }
            return _particleMaterial;
        }
    }

    /// <summary>Spawn a one-shot glowing burst (used for impacts and de-rez).</summary>
    public static void SpawnBurst(Vector3 position, Color color, int count, float speed = 4f, float size = 0.12f)
    {
        var go = new GameObject("Burst");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.6f;
        main.startSpeed = speed;
        main.startSize = size;
        // HDR color so bloom makes the burst glow.
        main.startColor = color * 2f;
        main.gravityModifier = 0.2f;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = ParticleMaterial;

        ps.Emit(count);
        Object.Destroy(go, 1.2f);
    }

    /// <summary>Create a glowing unlit material (HDR color feeds bloom).</summary>
    public static Material MakeGlowMaterial(Color color, float intensity = 3f)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", color * intensity);
        return mat;
    }
}
