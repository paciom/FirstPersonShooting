using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-character status controller for the expanded arsenal: slow, freeze,
/// stuck, float, blind, shrink, reveal, and knockback. Works on the player
/// (CharacterMotor), bots (NavMeshAgent + AIBrain), and dummies (visuals only).
/// Weapons call StatusEffects.Get(root).ApplyX(...) — the component is added on
/// demand and cleans itself up when effects expire or the character de-rezzes.
/// </summary>
public class StatusEffects : MonoBehaviour
{
    /// <summary>
    /// Voluntary speed scale the owner sets for itself (AIBrain dashes for a
    /// crate). Routed through here rather than written straight to
    /// NavMeshAgent.speed because this component rewrites that every frame —
    /// two writers would just fight each other.
    /// </summary>
    [HideInInspector] public float sprintMultiplier = 1f;

    /// <summary>
    /// Speed scale for vehicle form, owned by <see cref="TransformMode"/>. Kept
    /// separate from <see cref="sprintMultiplier"/> rather than sharing it —
    /// a bot dashing for a crate sets that one, and a transformed bot dashing
    /// for a crate is doing both at once.
    /// </summary>
    [HideInInspector] public float vehicleMultiplier = 1f;

    CharacterMotor _motor;
    NavMeshAgent _agent;
    AIBrain _brain;
    EnergyShield _shield;
    HoverBob _hoverBob;
    bool _hoverDisabled;

    float _baseAgentSpeed = -1f;
    float _baseAimError = -1f;
    Vector3 _baseScale;
    bool _scaleStored;

    float _slowUntil;
    float _slowFactor = 1f;
    float _hasteUntil;
    float _hasteFactor = 1f;
    float _freezeUntil;
    float _stuckUntil;
    float _floatUntil;
    float _blindUntil;
    float _shrinkUntil;
    float _revealUntil;

    GameObject _freezeShell;
    GameObject _bubbleShell;
    GameObject _hasteAura;
    readonly List<GameObject> _revealClones = new List<GameObject>();
    Material _revealMaterial;

    Vector3 _impulse;               // decaying knockback for NavMesh bots
    bool _agentDisabledForFloat;
    float _floatStartedAt = -1f;
    float _bubbleImmuneUntil;       // just-popped victims can't be instantly re-bubbled

    public static StatusEffects Get(Transform root)
    {
        if (root == null)
            return null;
        var fx = root.GetComponent<StatusEffects>();
        if (fx == null)
            fx = root.gameObject.AddComponent<StatusEffects>();
        return fx;
    }

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _agent = GetComponent<NavMeshAgent>();
        _brain = GetComponent<AIBrain>();
        _shield = GetComponent<EnergyShield>();
        _hoverBob = GetComponent<HoverBob>();
        if (_agent != null)
            _baseAgentSpeed = _agent.speed;
        if (_brain != null)
            _baseAimError = _brain.aimErrorDegrees;
        if (_shield != null)
            _shield.OnDeRezzed += ClearAll;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= ClearAll;
    }

    // ------------------------------------------------------------------ apply

    /// <summary>Frozen solid right now? (AIBrain checks this — no walking, turning, or firing in ice.)</summary>
    public bool IsFrozen => Time.time < _freezeUntil;

    /// <summary>Multiply move speed by `factor` for `duration` seconds (strongest slow wins).</summary>
    public void ApplySlow(float factor, float duration)
    {
        if (Time.time > _slowUntil)
            _slowFactor = 1f;
        _slowFactor = Mathf.Min(_slowFactor, factor);
        _slowUntil = Mathf.Max(_slowUntil, Time.time + duration);
    }

    /// <summary>
    /// Speed boost (Turbo Cells airdrop) — the mirror of ApplySlow, so it lands
    /// on the same moveFactor and works for the player's motor and a bot's
    /// NavMeshAgent alike. Strongest boost wins; it multiplies with any slow, so
    /// a turbo'd robot in goo just ends up somewhere in between.
    /// </summary>
    public void ApplyHaste(float factor, float duration)
    {
        if (Time.time > _hasteUntil)
            _hasteFactor = 1f;
        _hasteFactor = Mathf.Max(_hasteFactor, factor);
        _hasteUntil = Mathf.Max(_hasteUntil, Time.time + duration);

        if (_hasteAura == null)
        {
            _hasteAura = WeaponUtil.GhostShell(PrimitiveType.Capsule,
                transform.position + Vector3.up * 1f, new Vector3(1.25f, 1.2f, 1.25f),
                new Color(0.8f, 1f, 0.35f), 0.22f);
            _hasteAura.name = "HasteAura";
            _hasteAura.transform.SetParent(transform, true);
        }
    }

    public void ApplyFreeze(float duration)
    {
        _freezeUntil = Mathf.Max(_freezeUntil, Time.time + duration);
        if (_freezeShell == null)
        {
            _freezeShell = WeaponUtil.GhostShell(PrimitiveType.Cube,
                transform.position + Vector3.up * 1f, new Vector3(1.2f, 2.1f, 1.2f),
                new Color(0.55f, 0.85f, 1f), 0.7f);
            _freezeShell.name = "FreezeShell";
            _freezeShell.transform.SetParent(transform, true);
        }
    }

    /// <summary>Feet glued in place; look/aim still works.</summary>
    public void ApplyStuck(float duration)
    {
        _stuckUntil = Mathf.Max(_stuckUntil, Time.time + duration);
    }

    /// <summary>Anti-grav float; optional bubble shell for the Bubble Blower trap look.</summary>
    public void ApplyFloat(float duration, bool bubble = false)
    {
        if (bubble && Time.time < _bubbleImmuneUntil)
            return;   // just popped — brief immunity so chain-bubbling can't juggle forever

        if (Time.time >= _floatUntil)
            _floatStartedAt = Time.time;   // fresh float, start the continuous-air clock
        _floatUntil = Mathf.Max(_floatUntil, Time.time + duration);
        // Hard cap on continuous airtime, whatever keeps re-applying.
        _floatUntil = Mathf.Min(_floatUntil, _floatStartedAt + 3.5f);
        if (bubble && _bubbleShell == null)
        {
            // Soap film, not a lamp: additive shells stack, so overlapping
            // trapped robots must not white out the screen.
            _bubbleShell = WeaponUtil.GhostShell(PrimitiveType.Sphere,
                transform.position + Vector3.up * 1f, Vector3.one * 2.3f,
                new Color(0.55f, 0.8f, 1f), 0.18f);
            _bubbleShell.name = "BubbleShell";
            _bubbleShell.transform.SetParent(transform, true);
        }
    }

    /// <summary>Scramble AI aim (sand/sonic weapons). Harmless to the player character.</summary>
    public void ApplyBlind(float duration)
    {
        _blindUntil = Mathf.Max(_blindUntil, Time.time + duration);
        if (_brain != null && _baseAimError >= 0f)
            _brain.aimErrorDegrees = Mathf.Max(_brain.aimErrorDegrees, _baseAimError * 6f + 14f);
    }

    public void ApplyShrink(float duration)
    {
        if (!_scaleStored)
        {
            _baseScale = transform.localScale;
            _scaleStored = true;
        }
        _shrinkUntil = Mathf.Max(_shrinkUntil, Time.time + duration);
        transform.localScale = _baseScale * 0.45f;
        VfxUtil.ImpactBurst(transform.position + Vector3.up * 1f, new Color(0.8f, 0.4f, 1f));
    }

    /// <summary>Neon silhouette visible through walls for `duration` (Blacklight / Echo weapons).</summary>
    public void ApplyReveal(float duration, Color color)
    {
        _revealUntil = Mathf.Max(_revealUntil, Time.time + duration);
        if (_revealClones.Count > 0)
            return;   // already built; just extend the timer

        var shader = Shader.Find("PhotonArena/XRay");
        if (shader == null)
            return;
        _revealMaterial = new Material(shader);
        _revealMaterial.SetColor("_Color", color * 1.6f);
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null)
                continue;
            if (!IsBodyMesh(mf.transform))
                continue;   // don't silhouette the floor ring, shield bubble, or other overlays
            var clone = new GameObject("RevealSilhouette");
            clone.transform.SetParent(mf.transform, false);
            clone.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = clone.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _revealMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _revealClones.Add(clone);
        }
    }

    /// <summary>Knockback impulse (world-space). Player rides CharacterMotor; bots get agent.Move.</summary>
    public void AddImpulse(Vector3 impulse)
    {
        if (_motor != null)
            _motor.externalVelocity += impulse;
        else
            _impulse += impulse;
    }

    /// <summary>Direct pull/push displacement this frame (Magnet Ram, Black Hole).</summary>
    public void Drag(Vector3 displacement)
    {
        if (_motor != null)
            _motor.externalVelocity = displacement / Mathf.Max(Time.deltaTime, 0.001f) * 0.4f;
        else if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            _agent.Move(displacement);
        else
            transform.position += displacement;
    }

    // ------------------------------------------------------------------ tick

    void Update()
    {
        bool frozen = Time.time < _freezeUntil;
        bool stuck = Time.time < _stuckUntil;
        bool floating = Time.time < _floatUntil;
        bool slowed = Time.time < _slowUntil;
        bool hasted = Time.time < _hasteUntil;

        float moveFactor = Mathf.Max(0f, sprintMultiplier) * Mathf.Max(0f, vehicleMultiplier);
        if (slowed) moveFactor *= _slowFactor;
        if (hasted) moveFactor *= _hasteFactor;
        if (frozen || stuck) moveFactor = 0f;

        if (_motor != null)
        {
            _motor.statusSpeedMultiplier = moveFactor;
            _motor.floatMode = floating;
        }

        if (_agent != null)
        {
            if (floating)
            {
                if (!_agentDisabledForFloat)
                {
                    _agentDisabledForFloat = true;
                    _agent.enabled = false;
                }
                // Drift upward, capped below the wall line so bots stay in frame.
                if (transform.position.y < 2.2f)
                    transform.position += Vector3.up * (1.4f * Time.deltaTime);
            }
            else if (_agentDisabledForFloat)
            {
                LandAgent();
            }
            else if (_agent.enabled && _baseAgentSpeed > 0f)
            {
                _agent.speed = _baseAgentSpeed * moveFactor;
            }

            // Knockback for bots.
            if (_impulse.sqrMagnitude > 0.02f)
            {
                if (_agent.enabled && _agent.isOnNavMesh)
                    _agent.Move(_impulse * Time.deltaTime);
                else
                    transform.position += _impulse * Time.deltaTime;
                _impulse = Vector3.MoveTowards(_impulse, Vector3.zero, 14f * Time.deltaTime);
            }
        }

        // Things inside solid ice don't bob and sway.
        if (frozen && _hoverBob != null && _hoverBob.enabled)
        {
            _hoverBob.enabled = false;
            _hoverDisabled = true;
        }
        else if (!frozen && _hoverDisabled && _hoverBob != null)
        {
            _hoverBob.enabled = true;
            _hoverDisabled = false;
        }

        // Expiry cleanups.
        if (!hasted && _hasteAura != null)
            Destroy(_hasteAura);
        if (!frozen && _freezeShell != null)
            Destroy(_freezeShell);
        if (!floating && _bubbleShell != null)
        {
            VfxUtil.ImpactBurst(transform.position + Vector3.up * 1.4f, new Color(0.75f, 0.92f, 1f));
            Destroy(_bubbleShell);
            _bubbleImmuneUntil = Time.time + 2f;   // a beat on the ground before the next trap can take
        }
        if (Time.time >= _blindUntil && _brain != null && _baseAimError >= 0f && _brain.aimErrorDegrees > _baseAimError)
            _brain.aimErrorDegrees = _baseAimError;
        if (_scaleStored && Time.time >= _shrinkUntil)
        {
            transform.localScale = _baseScale;
            _scaleStored = false;
        }
        if (Time.time >= _revealUntil && _revealClones.Count > 0)
            ClearReveal();
    }

    void LandAgent()
    {
        _agentDisabledForFloat = false;
        // Drop to the surface below before handing control back to the NavMesh.
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 12f,
                ~0, QueryTriggerInteraction.Ignore))
            transform.position = hit.point;
        _agent.enabled = true;
        if (_agent.isOnNavMesh)
            _agent.Warp(transform.position);
    }

    /// <summary>
    /// True if this mesh is part of the character's actual body/gun — not a
    /// ground marker, shield bubble, freeze shell, or another reveal clone.
    /// </summary>
    bool IsBodyMesh(Transform mesh)
    {
        for (var t = mesh; t != null && t != transform; t = t.parent)
        {
            if (t.name == "TeamRing" || t.name == "ShieldBubble"
                || t.name == "FreezeShell" || t.name == "BubbleShell"
                || t.name == "HasteAura"
                || t.name == "XRaySilhouette" || t.name == "RevealSilhouette")
                return false;
        }
        return true;
    }

    void ClearReveal()
    {
        foreach (var clone in _revealClones)
            if (clone != null)
                Destroy(clone);
        _revealClones.Clear();
        if (_revealMaterial != null)
            Destroy(_revealMaterial);
    }

    /// <summary>Drop every active effect immediately (called on de-rez).</summary>
    public void ClearAll()
    {
        _slowUntil = _freezeUntil = _stuckUntil = _floatUntil = _blindUntil = _shrinkUntil = _revealUntil = 0f;
        _hasteUntil = 0f;
        _slowFactor = 1f;
        _hasteFactor = 1f;
        sprintMultiplier = 1f;
        _impulse = Vector3.zero;
        if (_hoverDisabled && _hoverBob != null)
        {
            _hoverBob.enabled = true;
            _hoverDisabled = false;
        }
        if (_freezeShell != null) Destroy(_freezeShell);
        if (_bubbleShell != null) Destroy(_bubbleShell);
        if (_hasteAura != null) Destroy(_hasteAura);
        ClearReveal();
        if (_motor != null)
        {
            _motor.statusSpeedMultiplier = 1f;
            _motor.floatMode = false;
        }
        if (_agentDisabledForFloat)
            LandAgent();
        if (_agent != null && _agent.enabled && _baseAgentSpeed > 0f)
            _agent.speed = _baseAgentSpeed;
        if (_brain != null && _baseAimError >= 0f)
            _brain.aimErrorDegrees = _baseAimError;
        if (_scaleStored)
        {
            transform.localScale = _baseScale;
            _scaleStored = false;
        }
    }
}
