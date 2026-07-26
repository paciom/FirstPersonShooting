using UnityEngine;

/// <summary>
/// A glowing line that fades out over a short lifetime — the instant "rail"
/// streak left by the Rail Zapper's hitscan shot.
/// </summary>
public class FadingLine : MonoBehaviour
{
    LineRenderer _line;
    Material _material;
    float _life;
    float _age;
    float _startWidth;
    float _startIntensity;

    public static void Spawn(Vector3 from, Vector3 to, Color color, float width, float life, float intensity = 4f)
    {
        SpawnPath(new[] { from, to }, color, width, life, intensity);
    }

    /// <summary>Polyline variant — lightning arcs, vines, zigzag screech lines.</summary>
    public static void SpawnPath(Vector3[] points, Color color, float width, float life, float intensity = 4f)
    {
        var go = new GameObject("FadingLine");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 2;
        var mat = VfxUtil.MakeAdditiveMaterial(null, color, intensity);
        line.material = mat;

        var fade = go.AddComponent<FadingLine>();
        fade._line = line;
        fade._material = mat;
        fade._life = life;
        fade._startWidth = width;
        fade._startIntensity = intensity;
    }

    /// <summary>A jagged lightning bolt between two points, jittered sideways per segment.</summary>
    public static void SpawnJagged(Vector3 from, Vector3 to, Color color, float width, float life,
        float jitter = 0.35f, int segments = 7, float intensity = 4f)
    {
        var points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 p = Vector3.Lerp(from, to, t);
            if (i > 0 && i < segments)
                p += Random.insideUnitSphere * jitter;
            points[i] = p;
        }
        SpawnPath(points, color, width, life, intensity);
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _life);
        if (t >= 1f)
        {
            Destroy(_material);
            Destroy(gameObject);
            return;
        }

        float k = 1f - t;
        _line.startWidth = _line.endWidth = _startWidth * k;
        _material.SetFloat("_Intensity", _startIntensity * k * k);
    }
}
