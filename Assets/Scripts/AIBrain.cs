using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Greybox bot: chases the player with a NavMeshAgent and shoots when it has
/// line of sight. Deliberately imperfect — reaction delay and an aim-error cone
/// make it fair for kids and fun to watch. Grows into behavior-tree
/// personalities (Rocketeer, Bastion, Phantom...) in Phase 2/3.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnergyShield))]
public class AIBrain : MonoBehaviour
{
    [Header("Combat")]
    public float sightRange = 30f;
    public float attackRange = 16f;
    public float reactionDelay = 0.35f;   // human-like: never instant
    public float aimErrorDegrees = 4f;    // never aimbot-precise
    public LaserBlaster blaster;

    [Header("Movement")]
    public float repathInterval = 0.4f;

    NavMeshAgent _agent;
    float _nextRepath;
    float _sawTargetAt = -1f;
    bool _active = true;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (blaster == null)
            blaster = GetComponentInChildren<LaserBlaster>();
    }

    /// <summary>Called by DeRezEffect while this bot is de-rezzed.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = !active;
        _sawTargetAt = -1f;
    }

    void Update()
    {
        if (!_active)
            return;

        var player = PlayerBrain.Local;
        if (player == null)
            return;

        Vector3 targetCenter = player.transform.position + Vector3.up * 1.2f;
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        float distance = Vector3.Distance(transform.position, player.transform.position);

        bool hasLineOfSight = false;
        if (distance < sightRange)
        {
            Vector3 toTarget = (targetCenter - eye).normalized;
            if (Physics.Raycast(eye, toTarget, out RaycastHit hit, sightRange, ~0, QueryTriggerInteraction.Ignore))
                hasLineOfSight = hit.transform.root == player.transform.root;
        }

        // Chase: keep pathing toward the player, stop at attack range with sight.
        if (Time.time >= _nextRepath && _agent.isOnNavMesh)
        {
            _nextRepath = Time.time + repathInterval;
            bool close = distance <= attackRange && hasLineOfSight;
            _agent.isStopped = close;
            if (!close)
                _agent.SetDestination(player.transform.position);
        }

        if (hasLineOfSight)
        {
            // Face the player while engaging.
            Vector3 flat = player.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(flat), 8f * Time.deltaTime);

            if (_sawTargetAt < 0f)
                _sawTargetAt = Time.time;   // reaction timer starts on first sight

            if (distance <= attackRange && Time.time - _sawTargetAt >= reactionDelay && blaster != null)
            {
                Vector3 aim = (targetCenter - blaster.muzzle.position).normalized;
                aim = Quaternion.Euler(
                    Random.Range(-aimErrorDegrees, aimErrorDegrees),
                    Random.Range(-aimErrorDegrees, aimErrorDegrees), 0f) * aim;
                blaster.TryFire(aim);
            }
        }
        else
        {
            _sawTargetAt = -1f;
        }
    }
}
