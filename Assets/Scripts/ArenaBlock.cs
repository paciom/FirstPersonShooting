using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A living cover block. Weapons can destroy it (it shatters and sinks away),
/// then it regrows from the ground later — lifting any character standing on
/// top — and occasionally slides to a new spot, sometimes slowly, sometimes in
/// a sudden dash. A NavMeshObstacle carves the navmesh as it moves so bots
/// re-path around it without a full rebake.
/// </summary>
[RequireComponent(typeof(NavMeshObstacle))]
public class ArenaBlock : MonoBehaviour
{
    public float maxHealth = 55f;
    public Color color = new Color(1f, 0.25f, 0.9f);

    enum State { Active, Sinking, Hidden, Rising, Moving }
    State _state = State.Active;

    public bool IsActive => _state == State.Active;
    public bool IsHidden => _state == State.Hidden;
    public Vector3 Home { get; set; }

    /// <summary>
    /// Raised the instant this block is shot out, with the world point it died
    /// at. ArenaBlockManager listens: every destroyed block regrows somewhere
    /// else, and sometimes coughs up a treasure on the way out.
    /// </summary>
    public event System.Action<ArenaBlock, Vector3> OnShattered;

    Renderer[] _renderers;
    NavMeshObstacle _obstacle;
    MaterialPropertyBlock _mpb;
    float _health;
    float _flash;

    // Tween state (shared by sink / rise / move).
    Vector3 _from, _to;
    float _t, _dur;
    readonly HashSet<NavMeshAgent> _liftedAgents = new HashSet<NavMeshAgent>();

    static readonly int EmissionId = Shader.PropertyToID("_SeamGlow");

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _obstacle = GetComponent<NavMeshObstacle>();
        _obstacle.carving = true;
        _mpb = new MaterialPropertyBlock();
        _health = maxHealth;
        Home = transform.position;
    }

    float HalfHeight => transform.localScale.y * 0.5f;
    float TopY => transform.position.y + HalfHeight;

    // ---------- Damage ----------

    public void TakeHit(float damage, Vector3 point)
    {
        if (_state != State.Active && _state != State.Moving)
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

    public void Regrow()
    {
        _health = maxHealth;
        SetVisible(true);
        transform.position = new Vector3(Home.x, Home.y - (transform.localScale.y + 0.6f), Home.z);
        _obstacle.enabled = true;
        BeginTween(transform.position, Home, 1.1f);
        _state = State.Rising;
    }

    public void SlideTo(Vector3 target, float duration)
    {
        if (_state != State.Active)
            return;
        BeginTween(transform.position, new Vector3(target.x, transform.position.y, target.z), duration);
        _state = State.Moving;
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
            case State.Rising: TickRise(); break;
            case State.Moving: TickMove(); break;
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

    void TickRise()
    {
        _t += Time.deltaTime / _dur;
        var p = transform.position;
        p.y = Mathf.Lerp(_from.y, _to.y, Mathf.SmoothStep(0f, 1f, _t));
        transform.position = p;

        LiftRiders();

        if (_t >= 1f)
        {
            transform.position = _to;
            ReleaseAgents();
            _state = State.Active;
        }
    }

    void TickMove()
    {
        _t += Time.deltaTime / _dur;
        Vector3 prev = transform.position;
        Vector3 next = Vector3.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _t));

        // Bots navigate on the navmesh and don't physically collide, so a sliding
        // block would clip straight through them. Halt against a robot in the way.
        if (BlockedByRobot(next))
        {
            _state = State.Active;
            return;
        }

        ShovePlayer(next - prev);
        transform.position = next;
        if (_t >= 1f)
            _state = State.Active;
    }

    // ---------- Character interaction ----------
    //
    // We test characters directly against the block's footprint rather than via
    // Physics.OverlapBox: a small fixed physics buffer near walls/other blocks
    // could fill with non-character colliders and miss a bot, which let a rising
    // block engulf it. Iterating the handful of characters is cheap and reliable.

    CharacterMotor[] _motors;
    NavMeshAgent[] _agents;
    float _nextCharacterRescan;

    void CacheCharacters()
    {
        // Refreshed on a slow tick rather than cached once: teams gain robots
        // mid-match now (gold-funded reinforcements), and a newcomer missing
        // from the cache would get engulfed by a rising block.
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
    /// manager picking somewhere to slide or regrow a block.
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

    void LiftRiders()
    {
        CacheCharacters();
        float topY = TopY;

        foreach (var m in _motors)
        {
            if (m == null) continue;
            Vector3 p = m.transform.position;
            if (p.y < topY && InsideFootprint(transform.position, p, 0.45f))
                m.Teleport(new Vector3(p.x, topY, p.z));
        }

        foreach (var a in _agents)
        {
            if (a == null) continue;
            Vector3 p = a.transform.position;
            if (p.y < topY && InsideFootprint(transform.position, p, 0.45f))
            {
                if (a.enabled)
                {
                    a.enabled = false;
                    _liftedAgents.Add(a);
                }
                a.transform.position = new Vector3(p.x, topY, p.z);
            }
        }
    }

    void ReleaseAgents()
    {
        foreach (var agent in _liftedAgents)
        {
            if (agent == null)
                continue;
            // Drop the bot back onto the nearest navmesh (edge of the block).
            if (NavMesh.SamplePosition(agent.transform.position, out var hit, 8f, NavMesh.AllAreas))
                agent.transform.position = hit.position;
            agent.enabled = true;
        }
        _liftedAgents.Clear();
    }

    void ShovePlayer(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-6f)
            return;
        CacheCharacters();
        foreach (var m in _motors)
        {
            if (m == null) continue;
            if (InsideFootprint(transform.position, m.transform.position, 0.5f))
                m.Teleport(m.transform.position + new Vector3(delta.x, 0f, delta.z));
        }
    }

    bool BlockedByRobot(Vector3 pos)
    {
        CacheCharacters();
        foreach (var a in _agents)
            if (a != null && a.enabled && InsideFootprint(pos, a.transform.position, 0.35f))
                return true;
        return false;
    }

    // ---------- Visuals ----------

    void ApplyFlash()
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            // Push the sci-fi seam glow up briefly when hit.
            _mpb.SetFloat(EmissionId, 1.5f + _flash * 6f);
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
