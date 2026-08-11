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

        yield return new WaitForSeconds(respawnDelay);

        // Re-materialize at the spawn point. NavMeshAgents must Warp — setting
        // transform.position while an agent is enabled makes it fight the move.
        var motor = GetComponent<CharacterMotor>();
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (motor != null)
            motor.Teleport(_spawnPosition);
        else if (agent != null && agent.enabled)
            agent.Warp(_spawnPosition);
        else
            transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

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
