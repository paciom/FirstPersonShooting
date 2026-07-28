using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A vehicle drives through cover. Robots have to shoot it.
///
/// This is the one mechanic here that outlives the moment it happens: a
/// shattered block is gone for the rest of the round, so a bot that charges
/// across the arena leaves a lane behind it and the map is a little more open
/// than it was. Cover shot down by a robot does the same thing, but slowly and
/// from a distance — ramming is how a route gets opened deliberately.
///
/// Overlap sweep rather than OnCollisionEnter: bots move on a NavMeshAgent,
/// which drives the transform directly and does not reliably raise collision
/// events against static cover. Sweeping a sphere just ahead of the hull each
/// frame catches every case the same way for bots and the player.
///
/// Damage scales with speed, so a vehicle that has stopped cannot grind a block
/// down by resting against it — ramming has to be a charge.
/// </summary>
[RequireComponent(typeof(TransformMode))]
public class VehicleRam : MonoBehaviour
{
    [Tooltip("Damage at full speed. ArenaBlock has 55 health, so a solid hit " +
             "takes most of a block and a sustained charge clears it.")]
    public float ramDamage = 40f;

    [Tooltip("Below this speed nothing is rammed, so parking against cover does nothing.")]
    public float minimumSpeed = 3f;

    [Tooltip("Speed at which ramDamage is reached in full.")]
    public float fullSpeed = 9f;

    [Tooltip("How far ahead of the hull the sweep reaches.")]
    public float reach = 1.1f;

    public float radius = 0.9f;

    [Tooltip("Seconds before the same block can be rammed again. Without this " +
             "a single pass would apply damage every frame it overlapped.")]
    public float perBlockCooldown = 0.4f;

    TransformMode _transform;
    Vector3 _lastPosition;
    float _speed;
    readonly Dictionary<ArenaBlock, float> _nextHit = new Dictionary<ArenaBlock, float>();
    static readonly Collider[] Hits = new Collider[16];

    void Awake()
    {
        _transform = GetComponent<TransformMode>();
        _lastPosition = transform.position;
    }

    void Update()
    {
        // Measured, not read off an agent: the player is a CharacterController
        // and the bots are NavMeshAgents, and this has to treat them alike.
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        _speed = (transform.position - _lastPosition).magnitude / dt;
        _lastPosition = transform.position;

        if (_transform == null || !_transform.IsVehicle || _transform.IsBusy)
            return;
        if (_speed < minimumSpeed)
            return;

        float scale = Mathf.InverseLerp(minimumSpeed, fullSpeed, _speed);
        float damage = ramDamage * Mathf.Clamp01(scale);
        if (damage <= 0f)
            return;

        Vector3 nose = transform.position + Vector3.up * 0.5f + transform.forward * reach;
        int count = Physics.OverlapSphereNonAlloc(nose, radius, Hits, ~0,
                                                  QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var block = Hits[i] != null ? Hits[i].GetComponentInParent<ArenaBlock>() : null;
            if (block == null || !block.IsActive)
                continue;
            if (_nextHit.TryGetValue(block, out float ready) && Time.time < ready)
                continue;

            _nextHit[block] = Time.time + perBlockCooldown;
            block.TakeHit(damage, nose);
            VfxUtil.ImpactBurst(nose, new Color(1f, 0.7f, 0.25f));
        }
    }
}
