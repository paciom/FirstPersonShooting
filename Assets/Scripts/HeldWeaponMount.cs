using UnityEngine;

/// <summary>
/// Keeps the visible blaster prop on the robot's shoulder, and takes it away
/// when the robot isn't a robot.
///
/// WHY FOLLOW A BONE RATHER THAN PARENT TO ONE. Parenting would be simpler and
/// would not survive a robot swap: RobotFactory.Reskin destroys the whole model
/// and builds the replacement, so anything parented into the old skeleton dies
/// with it. The prop therefore stays under Body — which outlives every reskin —
/// and rides the bone from there, re-finding it whenever the model is rebuilt.
///
/// WHY POSITION FROM THE BONE BUT ROTATION FROM THE CHARACTER. Rig bones do not
/// agree on which local axis points forward — a Mixamo-style shoulder is rolled
/// out along the arm — so inheriting bone rotation aims the gun into the
/// robot's armpit at a different angle on every model. Taking only the bone's
/// world POSITION and orienting to the character keeps the barrel pointing
/// where the robot is facing, which is also where its shots go.
///
/// Vehicle form hides it outright: the tank has its own mounted siege kit, so a
/// hand blaster hanging in the air above a tank is just a floating prop.
/// </summary>
public class HeldWeaponMount : MonoBehaviour
{
    [Tooltip("Object whose 'Model' child holds the rig; normally the character's Body.")]
    public Transform modelHolder;

    [Tooltip("Bone to ride. Falls back through the list until one is found, so " +
             "rigs that name things differently still mount somewhere sensible.")]
    public string[] boneNames = { "RightShoulder", "RightArm", "RightHand", "Spine02" };

    [Tooltip("Offset from the bone, in the CHARACTER's frame: +x is out to the " +
             "robot's right, +y up, +z forward.")]
    public Vector3 mountOffset = new Vector3(0.20f, 0.10f, 0.06f);

    [Tooltip("Aim relative to the character's facing.")]
    public Vector3 mountEuler = Vector3.zero;

    [Tooltip("Hide the prop while transformed. The tank carries its own guns.")]
    public bool hideInVehicleForm = true;

    Transform _bone;
    TransformMode _transformMode;
    Renderer[] _renderers;
    bool _hidden;

    /// <summary>
    /// The prop's own build-time rotation, which is what turns the imported
    /// model's longest axis down +Z. It differs per model — the blaster comes in
    /// lying on its side — so it has to be composed with the aim rather than
    /// replaced by it. Overwriting rotation outright is what left the gun
    /// pointing off into the air.
    /// </summary>
    Quaternion _modelAlignment = Quaternion.identity;

    void Awake()
    {
        if (modelHolder == null)
            modelHolder = transform.parent;
        _transformMode = GetComponentInParent<TransformMode>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _modelAlignment = transform.localRotation;
    }

    // LateUpdate, so the Animator has already posed the skeleton this frame;
    // reading a bone in Update lags the robot by a frame and the gun swims.
    void LateUpdate()
    {
        UpdateVisibility();

        // No bones listed means "just handle visibility" — that is the player's
        // first-person viewmodel, which hangs off the camera so it aims with the
        // view and must not be dragged onto a shoulder it cannot see.
        if (boneNames == null || boneNames.Length == 0)
            return;

        if (_bone == null)
            _bone = FindBone();
        if (_bone == null)
            return;

        Transform character = _transformMode != null ? _transformMode.transform : transform.root;
        transform.position = _bone.position + character.rotation * mountOffset;
        // Character facing, then the aim tweak, then the model's own alignment
        // last — that alignment is what makes the barrel the forward axis at
        // all, so anything applied after it would turn the gun off-axis again.
        transform.rotation = character.rotation * Quaternion.Euler(mountEuler) * _modelAlignment;
    }

    void UpdateVisibility()
    {
        if (!hideInVehicleForm || _transformMode == null)
            return;

        // Hidden for the whole fold as well as the vehicle itself: the prop has
        // nowhere sensible to sit once the robot stops being humanoid.
        bool hide = _transformMode.IsVehicle || _transformMode.IsBusy;
        if (hide == _hidden)
            return;
        _hidden = hide;

        foreach (var renderer in _renderers)
            if (renderer != null)
                renderer.enabled = !hide;
    }

    /// <summary>
    /// Locate the mount bone under the current model. Re-run whenever the
    /// cached bone goes null, which is exactly what a reskin causes.
    /// </summary>
    Transform FindBone()
    {
        Transform root = modelHolder != null ? modelHolder : transform.root;
        if (root == null)
            return null;

        foreach (string name in boneNames)
        {
            var found = FindDeep(root, name);
            if (found != null)
                return found;
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
