using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A living cover block. Weapons can destroy it (it shatters and sinks away);
/// its replacement then DROPS OUT OF THE SKY somewhere else, Brawl
/// cargo-rain style: a glowing ring marks the landing spot, the block falls
/// with gravity's acceleration, thuds down in a puff of dust and shoves
/// anyone underneath out of the way. A NavMeshObstacle carves the navmesh at
/// its resting spot so bots path around it without a full rebake.
///
/// Blocks used to also slide and dash across the floor between fights. That
/// churn is gone on purpose: cover that drifts reads as furniture rearranging
/// itself, where a crate falling out of the sky reads as an EVENT — the same
/// verdict Brawl's Cargo Rain already settled. The only way a block moves now
/// is down.
/// </summary>
[RequireComponent(typeof(NavMeshObstacle))]
public class ArenaBlock : MonoBehaviour
{
    public float maxHealth = 55f;
    public Color color = new Color(1f, 0.25f, 0.9f);

    /// <summary>Metres above its home a dropping block starts from.</summary>
    const float DropHeight = 12f;

    /// <summary>Seconds a drop takes, tuned to read as a real fall from that height.</summary>
    const float DropDuration = 1.05f;

    enum State { Active, Sinking, Hidden, Dropping }
    State _state = State.Active;

    public bool IsActive => _state == State.Active;
    public bool IsHidden => _state == State.Hidden;
    public Vector3 Home { get; set; }

    /// <summary>
    /// Raised the instant this block is shot out, with the world point it died
    /// at. ArenaBlockManager listens: every destroyed block drops back in
    /// somewhere else, and sometimes coughs up a treasure on the way out.
    /// </summary>
    public event System.Action<ArenaBlock, Vector3> OnShattered;

    Renderer[] _renderers;
    NavMeshObstacle _obstacle;
    MaterialPropertyBlock _mpb;
    GameObject _warning;
    float _health;
    float _flash;

    // Tween state (shared by sink / drop).
    Vector3 _from, _to;
    float _t, _dur;

    static readonly int EmissionId = Shader.PropertyToID("_SeamGlow");

    // Cover is not all sci-fi plating any more. _SeamGlow only exists on
    // PhotonArena/SciFiPanel, so a stone or brick block set it into the void and
    // never visibly reacted to being shot; PhotonArena/Surface answers to
    // _HitFlash instead. Setting a property the shader does not declare is
    // harmless, so both go out and whichever one is listening responds.
    static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");

    static readonly int ObjectSpaceId = Shader.PropertyToID("_ObjectSpace");

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _obstacle = GetComponent<NavMeshObstacle>();
        _obstacle.carving = true;
        _mpb = new MaterialPropertyBlock();
        _health = maxHealth;
        Home = transform.position;
        LockPatternToMesh();
    }

    void OnDestroy()
    {
        if (_warning != null)
            Destroy(_warning);
    }

    /// <summary>
    /// A cover block is the one thing in an arena that moves — it sinks away
    /// and falls back in. Both arena shaders tile their surface pattern in
    /// world space by default, which is what keeps two walls sharing a course
    /// of brick where they meet, but it means a moving block swims through a
    /// pattern that stays nailed to the arena.
    ///
    /// Set per-renderer rather than on the material so it also corrects blocks
    /// whose materials were authored before the toggle existed — no scene
    /// rebuild required.
    /// </summary>
    void LockPatternToMesh()
    {
        foreach (var r in _renderers)
        {
            if (r == null)
                continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ObjectSpaceId, 1f);
            r.SetPropertyBlock(_mpb);
        }
    }

    float HalfHeight => transform.localScale.y * 0.5f;

    // ---------- Damage ----------

    public void TakeHit(float damage, Vector3 point)
    {
        if (_state != State.Active)
            return;
        _health -= damage;
        _flash = 1f;
        VfxUtil.ImpactBurst(point, color);
        if (_health <= 0f)
            Shatter();
    }

    void Shatter()
    {
        Vector3 deathPoint = transform.position + Vector3.up * HalfHeight;
        VfxUtil.Explosion(deathPoint, color, 1.5f);
        _obstacle.enabled = false;
        BeginTween(transform.position, transform.position - Vector3.up * (transform.localScale.y + 1.2f), 0.3f);
        _state = State.Sinking;
        OnShattered?.Invoke(this, deathPoint);
    }

    // ---------- Manager commands ----------

    /// <summary>
    /// Fall out of the sky onto <see cref="Home"/>, cargo-rain style: warning
    /// ring first, then the fall, then the thud. Works from Hidden (a shattered
    /// block returning) and from Active (a reshuffle throwing it somewhere
    /// new — set Home before calling).
    /// </summary>
    public void DropIn()
    {
        _health = maxHealth;
        SetVisible(true);
        // Not an obstacle again until it lands: a carve hanging in mid-air
        // does nothing useful, and bots may legitimately cross the ring.
        _obstacle.enabled = false;
        transform.position = Home + Vector3.up * DropHeight;
        SpawnWarningRing();
        BeginTween(transform.position, Home, DropDuration);
        _state = State.Dropping;
    }

    void BeginTween(Vector3 from, Vector3 to, float dur)
    {
        _from = from; _to = to; _t = 0f; _dur = Mathf.Max(0.01f, dur);
    }

    // ---------- Update ----------

    void Update()
    {
        if (_flash > 0f)
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 3f);
            ApplyFlash();
        }

        switch (_state)
        {
            case State.Sinking: TickSink(); break;
            case State.Dropping: TickDrop(); break;
        }
    }

    void TickSink()
    {
        _t += Time.deltaTime / _dur;
        transform.position = Vector3.Lerp(_from, _to, _t);
        if (_t >= 1f)
        {
            SetVisible(false);
            _state = State.Hidden;
        }
    }

    void TickDrop()
    {
        _t += Time.deltaTime / _dur;
        // t² easing: starts slow, arrives fast — a fall, not an elevator.
        transform.position = Vector3.Lerp(_from, _to, Mathf.Clamp01(_t) * Mathf.Clamp01(_t));
        if (_t < 1f)
            return;

        transform.position = _to;
        Land();
    }

    void Land()
    {
        if (_warning != null)
        {
            Destroy(_warning);
            _warning = null;
        }
        ExpelResidents();
        _obstacle.enabled = true;
        VfxUtil.SpawnBurst(transform.position - Vector3.up * (HalfHeight * 0.8f),
            new Color(1f, 0.8f, 0.4f), 10, 3.5f, 0.12f);
        _state = State.Active;
    }

    /// <summary>
    /// The Brawl-style landing marker: a flat glowing disc on the ground under
    /// the falling block, so the drop is a telegraph rather than an ambush.
    /// </summary>
    void SpawnWarningRing()
    {
        _warning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _warning.name = "DropWarning";
        Destroy(_warning.GetComponent<Collider>());
        float diameter = Mathf.Max(transform.localScale.x, transform.localScale.z) * 1.3f;
        _warning.transform.position = new Vector3(Home.x, Home.y - HalfHeight + 0.03f, Home.z);
        _warning.transform.localScale = new Vector3(diameter, 0.012f, diameter);
        _warning.GetComponent<MeshRenderer>().sharedMaterial =
            ArenaMaterials.Emissive("block-drop-warning", new Color(1f, 0.55f, 0.15f), 2.2f);
    }

    // ---------- Character interaction ----------
    //
    // We test characters directly against the block's footprint rather than via
    // Physics.OverlapBox: a small fixed physics buffer near walls/other blocks
    // could fill with non-character colliders and miss a bot. Iterating the
    // handful of characters is cheap and reliable.

    CharacterMotor[] _motors;
    NavMeshAgent[] _agents;
    float _nextCharacterRescan;

    void CacheCharacters()
    {
        // Refreshed on a slow tick rather than cached once: teams gain robots
        // mid-match now (gold-funded reinforcements), and a newcomer missing
        // from the cache would get landed on by a dropping block.
        bool stale = Time.time >= _nextCharacterRescan;
        if (stale || _motors == null || _motors.Length == 0)
            _motors = FindObjectsByType<CharacterMotor>(FindObjectsSortMode.None);
        if (stale || _agents == null || _agents.Length == 0)
            _agents = FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
        if (stale)
            _nextCharacterRescan = Time.time + 3f;
    }

    /// <summary>
    /// Does this block stand on <paramref name="worldPoint"/> (plus a margin)?
    /// Asked by anything that needs open floor — airdrop landing spots, and the
    /// manager picking somewhere for a block to drop back in.
    ///
    /// Note this deliberately ignores <see cref="IsHidden"/>: a block that's
    /// currently sunk still owns its footprint, because it's coming back.
    /// </summary>
    public bool CoversPoint(Vector3 worldPoint, float margin)
    {
        return InsideFootprint(transform.position, worldPoint, margin)
            || InsideFootprint(Home, worldPoint, margin);
    }

    bool InsideFootprint(Vector3 center, Vector3 worldPos, float margin)
    {
        Vector3 half = transform.localScale * 0.5f;
        Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPos - center);
        return Mathf.Abs(local.x) <= half.x + margin && Mathf.Abs(local.z) <= half.z + margin;
    }

    /// <summary>
    /// Push anyone under a just-landed block out to its nearest edge. The
    /// warning ring is the fair notice; this is the guarantee nobody ends the
    /// frame inside the geometry.
    /// </summary>
    void ExpelResidents()
    {
        CacheCharacters();

        foreach (var m in _motors)
        {
            if (m == null) continue;
            Vector3 p = m.transform.position;
            Vector3 pushed = PushedClear(p, 0.6f);
            if (pushed != p)
                m.Teleport(pushed);
        }

        foreach (var a in _agents)
        {
            if (a == null || !a.enabled) continue;
            Vector3 p = a.transform.position;
            Vector3 pushed = PushedClear(p, 0.55f);
            if (pushed != p)
                a.Warp(NavMesh.SamplePosition(pushed, out var hit, 4f, NavMesh.AllAreas)
                    ? hit.position : pushed);
        }
    }

    /// <summary>
    /// Where <paramref name="worldPos"/> ends up if it must be clear of the
    /// footprint: unchanged when already outside, otherwise shoved out the
    /// nearest side.
    /// </summary>
    Vector3 PushedClear(Vector3 worldPos, float margin)
    {
        Vector3 half = transform.localScale * 0.5f;
        Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPos - transform.position);
        float escapeX = half.x + margin - Mathf.Abs(local.x);
        float escapeZ = half.z + margin - Mathf.Abs(local.z);
        if (escapeX <= 0f || escapeZ <= 0f)
            return worldPos;

        if (escapeX <= escapeZ)
            local.x += (local.x >= 0f ? 1f : -1f) * escapeX;
        else
            local.z += (local.z >= 0f ? 1f : -1f) * escapeZ;

        Vector3 world = transform.rotation * local + transform.position;
        return new Vector3(world.x, worldPos.y, world.z);
    }

    // ---------- Visuals ----------

    void ApplyFlash()
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            // Push the sci-fi seam glow up briefly when hit...
            _mpb.SetFloat(EmissionId, 1.5f + _flash * 6f);
            // ...and flash the non-emissive materials, which have no seam to push.
            _mpb.SetFloat(HitFlashId, _flash * 2.5f);
            r.SetPropertyBlock(_mpb);
        }
    }

    void SetVisible(bool visible)
    {
        foreach (var r in _renderers)
            if (r != null)
                r.enabled = visible;
    }
}
