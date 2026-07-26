using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Robot ⇄ ground-vehicle transformation, on the character root.
///
/// The fold itself is a skeletal animation baked per robot by TransformRigForge,
/// so this component owns everything the animation cannot: the wheels and
/// thrusters that scale in as the chassis closes up, the light burst at the
/// moment the silhouette stops reading as a robot, and the gameplay side of
/// being a vehicle — faster, lower, harder to hit, and down to a single forward
/// gun.
///
/// Vehicle mode is a trade rather than an upgrade: speed and a smaller hitbox
/// in exchange for most of the arsenal, which is what makes it the way to race
/// an airdrop rather than the way to win a firefight.
///
/// Robots whose model has no vehicle clips (the procedural blockbot, the
/// primitive fallback) report <see cref="CanTransform"/> false and ignore every
/// request, so callers never have to ask what kind of robot they are holding.
/// </summary>
public class TransformMode : MonoBehaviour
{
    /// <summary>
    /// Length of the fold. Shared with TransformRigForge, which bakes the clips
    /// to exactly this duration — the prop scale-in and the light burst are
    /// timed against it, so the two must not drift apart.
    /// </summary>
    public const float FoldSeconds = 0.55f;

    /// <summary>Animator bool the forged controller switches states on.</summary>
    public const string VehicleParameter = "Vehicle";

    /// <summary>
    /// How far into the fold the light burst fires and the vehicle mesh takes
    /// over. Deliberately before halfway: by this point the robot has crouched
    /// enough to sell the wind-up, but not far enough to look like a heap —
    /// the deep end of the fold is only ever seen on robots with no vehicle
    /// model, where the crouch is all there is.
    /// </summary>
    public const float SwapFraction = 0.42f;

    [Tooltip("Speed scale while driving. Routed through StatusEffects, which owns agent speed.")]
    public float vehicleSpeedMultiplier = 1.75f;

    [Tooltip("Hitbox height while driving, as a fraction of the robot's standing height.")]
    [Range(0.3f, 1f)] public float vehicleHeightFraction = 0.55f;

    [Tooltip("Colour of the transformation light burst; ArenaBuilder sets the team colour.")]
    public Color burstColor = new Color(0.2f, 0.9f, 1f);

    [Tooltip("Body transform the wheels and thrusters hang off; found automatically.")]
    public Transform body;

    /// <summary>Currently a vehicle (or folding into one)?</summary>
    public bool IsVehicle { get; private set; }

    /// <summary>Mid-fold. Requests made now are queued rather than dropped.</summary>
    public bool IsBusy => _fold != null;

    /// <summary>False for models with no forged vehicle clips.</summary>
    public bool CanTransform => ResolveAnimator() != null;

    Animator _animator;
    VehicleSkin _skin;               // swaps in the vehicle mesh, if this robot has one
    Transform _rig;                  // wheels + thrusters, scaled in during the fold
    Coroutine _fold;
    bool _pending;
    bool _hasPending;

    CharacterController _controller;
    NavMeshAgent _agent;
    CapsuleCollider _hitCapsule;
    StatusEffects _status;
    WeaponLoadout _loadout;
    EnergyShield _shield;

    float _standHeight, _standRadius;
    Vector3 _standCenter;
    float _capsuleHeight;
    Vector3 _capsuleCenter;
    float _agentHeight;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _agent = GetComponent<NavMeshAgent>();
        _hitCapsule = GetComponent<CapsuleCollider>();
        _loadout = GetComponent<WeaponLoadout>();
        _shield = GetComponent<EnergyShield>();
        _status = StatusEffects.Get(transform);

        if (body == null)
        {
            var found = transform.Find("Body");
            body = found != null ? found : transform;
        }
        _skin = GetComponentInChildren<VehicleSkin>();

        if (_controller != null)
        {
            _standHeight = _controller.height;
            _standRadius = _controller.radius;
            _standCenter = _controller.center;
        }
        if (_hitCapsule != null)
        {
            _capsuleHeight = _hitCapsule.height;
            _capsuleCenter = _hitCapsule.center;
        }
        if (_agent != null)
            _agentHeight = _agent.height;

        // A de-rezzed robot always comes back as a robot. Rematerializing as a
        // half-folded vehicle would strand it with a vehicle's hitbox and one gun.
        if (_shield != null)
            _shield.OnDeRezzed += ForceRobotForm;
    }

    void OnDestroy()
    {
        if (_shield != null)
            _shield.OnDeRezzed -= ForceRobotForm;
    }

    // ----------------------------------------------------------------- requests

    public void Toggle() => SetVehicle(!IsVehicle);

    /// <summary>
    /// Ask to be a vehicle (or a robot again). A request that arrives mid-fold
    /// is remembered and applied when the current one finishes rather than
    /// reversing it — the clips are one-way bakes, so cutting between them
    /// snaps the skeleton instead of easing it.
    /// </summary>
    public void SetVehicle(bool vehicle)
    {
        if (!CanTransform)
            return;
        if (IsBusy)
        {
            _hasPending = true;
            _pending = vehicle;
            return;
        }
        if (vehicle == IsVehicle)
            return;

        IsVehicle = vehicle;
        _fold = StartCoroutine(FoldRoutine(vehicle));
    }

    /// <summary>
    /// Snap back to robot form with no animation and no coroutine left running.
    ///
    /// Deactivating a GameObject kills its coroutines permanently, so anything
    /// that hides a character — a de-rez, a mode switch — has to be able to
    /// unwind this synchronously or the robot comes back as a fast, one-gun
    /// vehicle that never finished transforming. Safe to call anytime.
    /// </summary>
    public void ForceRobotForm()
    {
        if (_fold != null)
        {
            StopCoroutine(_fold);
            _fold = null;
        }
        _hasPending = false;
        IsVehicle = false;

        var animator = ResolveAnimator();
        if (animator != null)
            animator.SetBool(VehicleParameter, false);

        ScaleRig(0f);
        if (_skin != null)
            _skin.SetVehicle(false);
        ApplyTuning(false);
    }

    IEnumerator FoldRoutine(bool vehicle)
    {
        var animator = ResolveAnimator();
        if (animator != null)
            animator.SetBool(VehicleParameter, vehicle);

        EnsureRig();

        // Halfway through is where the silhouette stops being readable either
        // way — the right moment to cover the change in light.
        bool burst = false;
        for (float t = 0f; t < 1f; t += Time.deltaTime / FoldSeconds)
        {
            if (!burst && t >= SwapFraction)
            {
                burst = true;
                VfxUtil.Explosion(transform.position + Vector3.up * 0.8f, burstColor, 1.1f);
                // Inside the flash: the robot mesh goes, the vehicle arrives.
                if (_skin != null)
                    _skin.SetVehicle(vehicle);
            }
            // Props trail the fold: nothing appears until the legs are on their
            // way in, so wheels never hang off a standing robot.
            ScaleRig(Mathf.Clamp01((vehicle ? t : 1f - t) * 2.2f - 1.2f));
            yield return null;
        }

        ScaleRig(vehicle ? 1f : 0f);
        ApplyTuning(vehicle);
        _fold = null;

        if (_hasPending)
        {
            _hasPending = false;
            SetVehicle(_pending);
        }
    }

    // ------------------------------------------------------------------ tuning

    void ApplyTuning(bool vehicle)
    {
        if (_status != null)
            _status.vehicleMultiplier = vehicle ? vehicleSpeedMultiplier : 1f;

        if (_loadout != null)
            _loadout.SetVehicleMode(vehicle);

        float f = vehicleHeightFraction;

        // Keep the feet where they are while the capsule shrinks: a controller
        // resized around its own centre would drop the character into the floor
        // and get shoved back out.
        if (_controller != null)
        {
            _controller.height = vehicle ? _standHeight * f : _standHeight;
            _controller.radius = vehicle ? _standRadius : _standRadius;
            _controller.center = vehicle
                ? new Vector3(_standCenter.x, _standHeight * f * 0.5f, _standCenter.z)
                : _standCenter;
        }

        if (_hitCapsule != null)
        {
            _hitCapsule.height = vehicle ? _capsuleHeight * f : _capsuleHeight;
            _hitCapsule.center = vehicle
                ? new Vector3(_capsuleCenter.x, _capsuleHeight * f * 0.5f, _capsuleCenter.z)
                : _capsuleCenter;
        }

        // Height only — the agent's radius feeds avoidance and pathfinding, and
        // changing it mid-match makes bots rethink routes they are already on.
        if (_agent != null && _agentHeight > 0f)
            _agent.height = vehicle ? _agentHeight * f : _agentHeight;
    }

    // -------------------------------------------------------------------- props

    /// <summary>
    /// Wheels, thrusters and a nose light, parented to Body rather than to
    /// bones. Body space is normalized to a 1.6m robot for every model in the
    /// roster, so one set of offsets fits all nine — bone space does not, since
    /// the rigs disagree on both scale and bone placement.
    /// </summary>
    void EnsureRig()
    {
        if (_rig != null)
            return;

        var go = new GameObject("VehicleRig");
        _rig = go.transform;
        _rig.SetParent(body, false);

        foreach (int side in new[] { -1, 1 })
        {
            foreach (float z in new[] { -0.40f, 0.40f })
                Wheel(new Vector3(0.34f * side, -0.85f, z));
            Thruster(new Vector3(0.22f * side, -0.62f, -0.58f));
        }
        Glow("NoseLight", new Vector3(0f, -0.60f, 0.62f), 0.34f, 1.7f);

        ScaleRig(0f);
    }

    void Wheel(Vector3 position)
    {
        var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "Wheel";
        DestroyCollider(wheel);
        wheel.transform.SetParent(_rig, false);
        wheel.transform.localPosition = position;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);  // axle along X
        wheel.transform.localScale = new Vector3(0.30f, 0.06f, 0.30f);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.10f, 0.11f, 0.13f));
        material.SetFloat("_Smoothness", 0.35f);
        wheel.GetComponent<MeshRenderer>().sharedMaterial = material;

        // Glowing hub, so the wheels read on camera against a dark floor.
        var hub = Glow("Hub", Vector3.zero, 0.20f, 1.6f);
        hub.SetParent(wheel.transform, false);
        hub.localPosition = Vector3.zero;
        hub.localRotation = Quaternion.identity;
    }

    void Thruster(Vector3 position)
    {
        var glow = Glow("Thruster", position, 0.30f, 2.2f);
        glow.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    /// <summary>Additive sprite quad, the same trick the team rings use.</summary>
    Transform Glow(string name, Vector3 position, float size, float intensity)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        DestroyCollider(quad);
        quad.transform.SetParent(_rig, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = Vector3.one * size;

        var material = new Material(Shader.Find("PhotonArena/Additive"));
        material.SetTexture("_MainTex", Resources.Load<Texture2D>("VFX/glow"));
        material.SetColor("_Color", burstColor);
        material.SetFloat("_Intensity", intensity);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        return quad.transform;
    }

    static void DestroyCollider(GameObject go)
    {
        var collider = go.GetComponent<Collider>();
        if (collider == null)
            return;
        if (Application.isPlaying) Destroy(collider);
        else DestroyImmediate(collider);
    }

    void ScaleRig(float amount)
    {
        if (_rig == null)
            return;
        _rig.gameObject.SetActive(amount > 0.001f);
        _rig.localScale = Vector3.one * Mathf.Clamp01(amount);
    }

    /// <summary>
    /// The model — and with it the Animator — is replaced whenever a player
    /// picks a different robot, so this is resolved on demand rather than
    /// cached. Returns null unless the controller actually has vehicle clips.
    /// </summary>
    Animator ResolveAnimator()
    {
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();
        if (_animator == null || _animator.runtimeAnimatorController == null)
            return null;

        foreach (var parameter in _animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Bool &&
                parameter.name == VehicleParameter)
                return _animator;
        return null;
    }
}
