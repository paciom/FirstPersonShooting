using UnityEngine;

/// <summary>
/// The pieces a broken block flies apart into: a handful of small cubes in
/// the parent's own material, tossed out ballistically, settling on the deck
/// and shrinking away inside a second. Pure theatre — no colliders, no
/// registry, nothing another system can see; a chunk that outlived its
/// second would just be a rock the tanks mysteriously drive through.
/// </summary>
public class TankDebris : MonoBehaviour
{
    const float Life = 0.9f;
    const float Gravity = 22f;

    Vector3 _velocity;
    float _age;
    float _rest;
    Vector3 _spin;
    Vector3 _baseScale;

    /// <summary>Scatter chunks where a block just died, sized to what died there.</summary>
    public static void Burst(Vector3 at, Vector3 blockScale, Material material, Transform parent)
    {
        int count = Random.Range(4, 7);
        float chunk = Mathf.Clamp(Mathf.Max(blockScale.x, blockScale.z) * 0.22f, 0.25f, 0.7f);
        for (int i = 0; i < count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Debris";
            Object.Destroy(go.GetComponent<Collider>());
            if (material != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.SetParent(parent, false);
            go.transform.position = at + new Vector3(
                Random.Range(-0.5f, 0.5f), Random.Range(0.3f, 1f), Random.Range(-0.5f, 0.5f));
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * (chunk * Random.Range(0.7f, 1.3f));

            var debris = go.AddComponent<TankDebris>();
            float angle = Random.Range(0f, Mathf.PI * 2f);
            debris._velocity = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                * Random.Range(2f, 5f) + Vector3.up * Random.Range(3f, 6f);
            debris._rest = go.transform.localScale.y * 0.5f;
            debris._spin = Random.onUnitSphere * Random.Range(180f, 540f);
            debris._baseScale = go.transform.localScale;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _age += dt;
        if (_age >= Life)
        {
            Destroy(gameObject);
            return;
        }

        _velocity.y -= Gravity * dt;
        var position = transform.position + _velocity * dt;
        if (position.y <= _rest)
        {
            // Landed: kill the fall and most of the slide, stop tumbling.
            position.y = _rest;
            _velocity = new Vector3(_velocity.x * 0.4f, 0f, _velocity.z * 0.4f);
            _spin = Vector3.zero;
        }
        transform.position = position;
        if (_spin != Vector3.zero)
            transform.Rotate(_spin * dt, Space.Self);

        // The last third of a chunk's life is its exit.
        float fade = Mathf.Clamp01((Life - _age) / (Life * 0.35f));
        if (fade < 1f)
            transform.localScale = _baseScale * Mathf.Max(0.02f, Mathf.Sqrt(fade));
    }
}
