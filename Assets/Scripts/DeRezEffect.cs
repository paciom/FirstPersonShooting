using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the visual "de-rez → wait → re-materialize at spawn" cycle when the
/// EnergyShield on this object hits zero. Scales the body down in a burst of
/// light, disables control, then teleports back to the spawn point and scales
/// back up. Works for the player, AI bots, and target dummies.
/// </summary>
[RequireComponent(typeof(EnergyShield))]
public class DeRezEffect : MonoBehaviour
{
    [Tooltip("Visual body to shrink; falls back to this transform's first child or itself.")]
    public Transform body;
    public float respawnDelay = 3f;
    public Color burstColor = new Color(0.2f, 0.9f, 1f);

    /// <summary>
    /// While true a de-rez is FINAL: the body dissolves and stays gone until
    /// something revives it. This is the single switch that turns Gunfight from
    /// a sandbox into a match — with everyone coming back three seconds later,
    /// "last team standing" can never happen and nothing can ever end.
    /// <see cref="GunfightMatch"/> owns it, and clears it on the way out; every
    /// other mode leaves it off, where the respawn IS the no-death fiction.
    /// </summary>
    public static bool Elimination { get; set; }

    EnergyShield _shield;
    Vector3 _spawnPosition;
    Quaternion _spawnRotation;
    Vector3 _bodyScale;

    void Awake()
    {
        _shield = GetComponent<EnergyShield>();
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        if (body == null)
            body = transform.childCount > 0 ? transform.GetChild(0) : transform;
        _bodyScale = body.localScale;

        _shield.OnDeRezzed += HandleDeRez;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= HandleDeRez;
    }

    /// <summary>
    /// Re-point where this character re-materializes. The spawn is captured at
    /// Awake, which is right for a fixed arena and wrong the moment arenas can
    /// be swapped: without this, everyone comes back at the previous arena's
    /// coordinates — usually inside a wall. Called by ArenaRuntime on load.
    /// </summary>
    public void SetSpawn(Vector3 position, Quaternion rotation)
    {
        _spawnPosition = position;
        _spawnRotation = rotation;
    }

    void HandleDeRez()
    {
        StartCoroutine(DeRezRoutine());
    }

    /// <summary>
    /// Force-complete an in-flight de-rez cycle. Deactivating a GameObject
    /// kills its coroutines permanently, so the mode controller calls this
    /// before hiding characters (and on every mode entry) to avoid stranding
    /// them invisible/invulnerable mid-cycle. Safe to call anytime.
    /// </summary>
    public void CancelAndRestore()
    {
        StopAllCoroutines();
        if (body != null)
            body.localScale = _bodyScale;
        SetControlEnabled(true);
        if (_shield.IsDown)
            _shield.Rematerialize();
    }

    IEnumerator DeRezRoutine()
    {
        VfxUtil.EnergyBurst(transform.position + Vector3.up * 1f, burstColor, 1.3f);
        SetControlEnabled(false);

        // Shrink into nothing — dissolving into light, never dying.
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
        {
            body.localScale = _bodyScale * (1f - t);
            yield return null;
        }
        body.localScale = Vector3.zero;

        // Out for the round. Control and colliders went off at the top of this
        // routine, so the shell just sits there being nobody until the referee
        // stands it up again — see GunfightMatch.
        if (Elimination)
            yield break;

        yield return new WaitForSeconds(respawnDelay);

        MoveTo(_spawnPosition, _spawnRotation);

        VfxUtil.EnergyBurst(_spawnPosition + Vector3.up * 1f, burstColor, 0.7f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            body.localScale = _bodyScale * t;
            yield return null;
        }
        body.localScale = _bodyScale;

        SetControlEnabled(true);
        _shield.Rematerialize();
    }

    /// <summary>
    /// Put this character somewhere, whatever is driving it. NavMeshAgents must
    /// Warp and CharacterControllers must Teleport — setting transform.position
    /// under either one makes it fight the move.
    /// </summary>
    void MoveTo(Vector3 position, Quaternion rotation)
    {
        var motor = GetComponent<CharacterMotor>();
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (motor != null)
            motor.Teleport(position);
        else if (agent != null && agent.enabled)
            agent.Warp(position);
        else
            transform.position = position;
        transform.rotation = rotation;
    }

    /// <summary>
    /// Stand this character back up at its spawn, whole: the round reset. Also
    /// unwinds a de-rez in flight, so a robot that fell as the round ended does
    /// not finish dissolving into the next one.
    /// </summary>
    public void ReviveAtSpawn() => Revive(_spawnPosition, _spawnRotation);

    /// <summary>
    /// Stand up somewhere else entirely — the player taking over a team-mate's
    /// body in Player v AI.
    /// </summary>
    public void Revive(Vector3 position, Quaternion rotation)
    {
        StopAllCoroutines();
        MoveTo(position, rotation);
        if (body != null)
            body.localScale = _bodyScale;
        SetControlEnabled(true);
        if (_shield.IsDown)
            _shield.Rematerialize();
        VfxUtil.EnergyBurst(position + Vector3.up * 1f, burstColor, 0.7f);
    }

    void SetControlEnabled(bool enabled)
    {
        var playerBrain = GetComponent<PlayerBrain>();
        if (playerBrain != null) playerBrain.enabled = enabled;

        var aiBrain = GetComponent<AIBrain>();
        if (aiBrain != null) aiBrain.SetActive(enabled);

        // Clear stale input so a de-rezzed player doesn't keep gliding.
        if (!enabled)
        {
            var motor = GetComponent<CharacterMotor>();
            if (motor != null)
                motor.SetMoveInput(Vector2.zero);
        }

        foreach (var col in GetComponentsInChildren<Collider>())
            if (!(col is CharacterController))
                col.enabled = enabled;
    }
}
