using UnityEngine;

/// <summary>
/// A camera-facing quad that scales up and fades out — the workhorse of
/// explosion flashes and shockwave rings.
/// </summary>
public class FlashQuad : MonoBehaviour
{
    Material _material;
    float _life;
    float _age;
    float _startScale;
    float _endScale;
    float _startIntensity;

    public static void Spawn(Vector3 position, Texture2D texture, Color color,
        float startScale, float endScale, float life, float intensity = 2.5f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "FlashQuad";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = position;

        var mat = new Material(Shader.Find("PhotonArena/Additive"));
        mat.SetTexture("_MainTex", texture);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Intensity", intensity);
        go.GetComponent<MeshRenderer>().material = mat;

        var flash = go.AddComponent<FlashQuad>();
        flash._material = mat;
        flash._life = life;
        flash._startScale = startScale;
        flash._endScale = endScale;
        flash._startIntensity = intensity;
        go.transform.localScale = Vector3.one * startScale;
    }

    void LateUpdate()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _life);
        if (t >= 1f)
        {
            Destroy(_material);
            Destroy(gameObject);
            return;
        }

        // Ease-out growth, quadratic fade.
        float grow = 1f - (1f - t) * (1f - t);
        transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, grow);
        _material.SetFloat("_Intensity", _startIntensity * (1f - t) * (1f - t));

        var cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
