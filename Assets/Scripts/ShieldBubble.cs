using UnityEngine;

/// <summary>
/// Force-field bubble around a character: invisible when idle, flashes its
/// scrolling hex lattice when the EnergyShield takes a hit, big flare on
/// de-rez. Pure camera candy — no gameplay collision.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class ShieldBubble : MonoBehaviour
{
    public Color color = new Color(0.2f, 0.9f, 1f);
    public float hitFlash = 1.4f;
    public float fadePerSecond = 3.2f;

    EnergyShield _shield;
    Material _material;
    float _intensity;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _shield.OnDamaged += HandleDamaged;
        _shield.OnDeRezzed += HandleDeRezzed;
    }

    void Start()
    {
        // Parent under the Body rig so the bubble scales away during de-rez.
        var parent = transform.Find("Body") ?? transform;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "ShieldBubble";
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * 1.9f;

        _material = new Material(Shader.Find("PhotonArena/ForceField"));
        _material.SetTexture("_MainTex", Resources.Load<Texture2D>("VFX/hex"));
        _material.SetColor("_Color", color);
        _material.SetFloat("_Intensity", 0f);
        sphere.GetComponent<MeshRenderer>().material = _material;
    }

    void OnDestroy()
    {
        if (_shield != null)
        {
            _shield.OnDamaged -= HandleDamaged;
            _shield.OnDeRezzed -= HandleDeRezzed;
        }
    }

    void HandleDamaged(float damage, Vector3 hitPoint)
    {
        _intensity = Mathf.Max(_intensity, hitFlash);
    }

    void HandleDeRezzed()
    {
        _intensity = 3f;
    }

    void Update()
    {
        if (_material == null)
            return;
        _intensity = Mathf.MoveTowards(_intensity, 0f, fadePerSecond * Time.deltaTime);
        _material.SetFloat("_Intensity", _intensity);
    }
}
