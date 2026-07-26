using UnityEngine;

/// <summary>
/// Floating shield bar over a robot's head: hidden until the robot takes a
/// hit, then visible for a few seconds, billboarded to the camera, draining
/// from team color toward warning orange. Self-bootstraps onto every shielded
/// character at play time (and picks up late spawns like decoys), so no scene
/// wiring is needed. The player is skipped — they have the HUD bar.
/// </summary>
public class FloatingShieldBar : MonoBehaviour
{
    const float VisibleSeconds = 3f;
    const float BarWidth = 0.9f;
    const float Height = 2.55f;

    static readonly Color WarnOrange = new Color(1f, 0.45f, 0.1f);

    EnergyShield _shield;
    Transform _holder;
    Transform _fill;
    Material _fillMaterial;
    Color _teamColor;
    float _visibleUntil = -1f;

    // ------------------------------------------------------------- bootstrap

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("ShieldBarSpawner");
        go.AddComponent<ShieldBarSpawner>();
    }

    /// <summary>Attaches bars to every shielded character; rescans for late spawns (decoys).</summary>
    class ShieldBarSpawner : MonoBehaviour
    {
        float _nextScan;

        void Update()
        {
            if (Time.time < _nextScan)
                return;
            _nextScan = Time.time + 2f;
            foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
            {
                if (shield.GetComponent<FloatingShieldBar>() != null)
                    continue;
                if (shield.GetComponent<PlayerBrain>() != null)
                    continue;   // the player reads their shield off the HUD
                shield.gameObject.AddComponent<FloatingShieldBar>();
            }
        }
    }

    // ------------------------------------------------------------- lifecycle

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _teamColor = _shield != null && _shield.teamId == 1
            ? new Color(1f, 0.25f, 0.9f)
            : new Color(0.2f, 0.9f, 1f);
        BuildBar();
        if (_shield != null)
        {
            _shield.OnDamaged += HandleDamaged;
            _shield.OnDeRezzed += HandleDeRezzed;
        }
    }

    void OnDestroy()
    {
        if (_shield != null)
        {
            _shield.OnDamaged -= HandleDamaged;
            _shield.OnDeRezzed -= HandleDeRezzed;
        }
    }

    void HandleDamaged(float damage, Vector3 hitPoint) => _visibleUntil = Time.time + VisibleSeconds;

    void HandleDeRezzed() => _visibleUntil = -1f;   // no bar over an empty spot

    void BuildBar()
    {
        _holder = new GameObject("ShieldBar").transform;
        _holder.SetParent(transform, false);
        _holder.localPosition = Vector3.up * Height;

        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(back.GetComponent<Collider>());
        back.name = "Back";
        back.transform.SetParent(_holder, false);
        back.transform.localScale = new Vector3(BarWidth, 0.13f, 1f);
        back.GetComponent<MeshRenderer>().material =
            VfxUtil.MakeGlowMaterial(new Color(0.03f, 0.04f, 0.06f), 1f);

        var fillGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(fillGo.GetComponent<Collider>());
        fillGo.name = "Fill";
        _fill = fillGo.transform;
        _fill.SetParent(_holder, false);
        _fill.localPosition = new Vector3(0f, 0f, -0.01f);   // in front of the back plate
        _fill.localScale = new Vector3(BarWidth - 0.06f, 0.08f, 1f);
        _fillMaterial = VfxUtil.MakeGlowMaterial(_teamColor, 1.6f);
        fillGo.GetComponent<MeshRenderer>().material = _fillMaterial;

        _holder.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        bool visible = Time.time < _visibleUntil && _shield != null && !_shield.IsDown;
        if (_holder.gameObject.activeSelf != visible)
            _holder.gameObject.SetActive(visible);
        if (!visible)
            return;

        // Face the active camera.
        var cam = Camera.main;
        if (cam != null)
            _holder.rotation = Quaternion.LookRotation(_holder.position - cam.transform.position);

        // Fill drains left-anchored, cooling from team color to warning orange.
        float n = _shield.Normalized;
        float fullWidth = BarWidth - 0.06f;
        _fill.localScale = new Vector3(fullWidth * Mathf.Max(0.001f, n), 0.08f, 1f);
        _fill.localPosition = new Vector3(-fullWidth * (1f - n) * 0.5f, 0f, -0.01f);
        _fillMaterial.SetColor("_BaseColor", Color.Lerp(WarnOrange, _teamColor, n) * 1.6f);
    }
}
