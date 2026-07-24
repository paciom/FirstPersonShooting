using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Bot brain: hunts the nearest enemy (any EnergyShield on another team — the
/// player or an opposing bot), which makes Player-vs-AI and AI-vs-AI the same
/// code path. Deliberately imperfect: reaction delay and an aim-error cone keep
/// it fair for kids and fun to watch. Grows into behavior-tree personalities
/// (Rocketeer, Bastion, Phantom...) in Phase 2/3.
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
    EnergyShield _myShield;
    EnergyShield _target;
    float _nextRepath;
    float _nextTargetScan;
    float _sawTargetAt = -1f;
    bool _active = true;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _myShield = GetComponent<EnergyShield>();
        if (blaster == null)
            blaster = GetComponentInChildren<LaserBlaster>();
    }

    /// <summary>Called by DeRezEffect while de-rezzed and by the mode controller in menus.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = !active;
        _sawTargetAt = -1f;
        _target = null;
    }

    void Update()
    {
        if (!_active)
            return;

        if (Time.time >= _nextTargetScan)
        {
            _nextTargetScan = Time.time + 0.5f;
            _target = FindNearestEnemy();
        }

        if (!IsValidTarget(_target))
        {
            _sawTargetAt = -1f;
            return;
        }

        Vector3 targetCenter = _target.transform.position + Vector3.up * 1.2f;
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        float distance = Vector3.Distance(transform.position, _target.transform.position);

        bool hasLineOfSight = false;
        if (distance < sightRange)
        {
            Vector3 toTarget = (targetCenter - eye).normalized;
            if (Physics.Raycast(eye, toTarget, out RaycastHit hit, sightRange, ~0, QueryTriggerInteraction.Ignore))
                hasLineOfSight = hit.transform.root == _target.transform.root;
        }

        // Chase: keep pathing toward the target, stop at attack range with sight.
        if (Time.time >= _nextRepath && _agent.isOnNavMesh)
        {
            _nextRepath = Time.time + repathInterval;
            bool close = distance <= attackRange && hasLineOfSight;
            _agent.isStopped = close;
            if (!close)
                _agent.SetDestination(_target.transform.position);
        }

        if (hasLineOfSight)
        {
            // Face the target while engaging.
            Vector3 flat = _target.transform.position - transform.position;
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

    bool IsValidTarget(EnergyShield shield)
    {
        return shield != null
            && shield.gameObject.activeInHierarchy
            && !shield.IsDown
            && shield.teamId != _myShield.teamId;
    }

    EnergyShield FindNearestEnemy()
    {
        EnergyShield best = null;
        float bestDistance = float.MaxValue;
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (!IsValidTarget(shield))
                continue;
            float d = (shield.transform.position - transform.position).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = shield;
            }
        }
        return best;
    }
}
