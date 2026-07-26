using UnityEngine;

/// <summary>
/// Swaps the robot mesh for a purpose-built ground-vehicle mesh, hidden under
/// the transformation's light burst.
///
/// WHY A SWAP AT ALL. Folding the rig gets the robot into a compact crouch and
/// no further — there is no hull, no wheels and no chassis anywhere in a
/// humanoid mesh to fold out of it. The fold is the wind-up; this is the
/// payoff. Robots with no vehicle model just fold, which is why every call here
/// is safe on a null prefab.
///
/// Renderers are toggled rather than GameObjects on purpose: the robot's
/// Animator lives on its model, and deactivating that object would reset the
/// state machine to its default state — so the unfold would never play, because
/// re-enabling mid-ToRobot would snap it straight back to Locomotion.
///
/// The vehicle is measured against the robot while the robot is still STANDING
/// (in Start, before anything folds) — bounds taken mid-fold would size every
/// vehicle against a crouch.
/// </summary>
public class VehicleSkin : MonoBehaviour
{
    [Tooltip("Ground-vehicle model for the robot currently worn. Null = fold only.")]
    public GameObject vehiclePrefab;

    [Tooltip("Transform whose 'Model' child is the robot; defaults to this one.")]
    public Transform holder;

    [Tooltip("Team tint, applied the same way RobotFactory tints robot models.")]
    public Color tint = Color.white;

    [Tooltip("Vehicle height as a fraction of the standing robot's height.")]
    [Range(0.2f, 1.5f)] public float heightFraction = 0.62f;

    public bool HasVehicle => vehiclePrefab != null;

    /// <summary>Currently showing the vehicle rather than the robot?</summary>
    public bool IsShowingVehicle { get; private set; }

    GameObject _vehicle;
    Renderer[] _robotRenderers;
    bool _built;

    void Start()
    {
        Build();
    }

    /// <summary>
    /// Point at a different vehicle model — the runtime robot swap on the
    /// select screen changes which robot is worn, and with it which vehicle it
    /// should turn into.
    /// </summary>
    public void SetVehiclePrefab(GameObject prefab, Color teamTint)
    {
        if (_vehicle != null)
            Destroy(_vehicle);
        _vehicle = null;
        _built = false;
        vehiclePrefab = prefab;
        tint = teamTint;
        IsShowingVehicle = false;
        Build();
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        if (holder == null)
            holder = transform;

        var robot = FindNewestModel();
        if (robot == null)
            return;
        _robotRenderers = robot.GetComponentsInChildren<Renderer>(true);

        if (vehiclePrefab == null || _robotRenderers.Length == 0)
            return;

        Bounds robotBounds = Combine(_robotRenderers);

        _vehicle = Instantiate(vehiclePrefab, holder);
        _vehicle.name = "VehicleModel";
        _vehicle.transform.localPosition = Vector3.zero;
        _vehicle.transform.localRotation = Quaternion.identity;
        _vehicle.transform.localScale = Vector3.one;

        var vehicleRenderers = _vehicle.GetComponentsInChildren<Renderer>(true);
        if (vehicleRenderers.Length == 0)
        {
            Destroy(_vehicle);
            _vehicle = null;
            return;
        }

        // Generated models don't agree on which way is forward. A ground
        // vehicle is longer than it is wide, so the long horizontal axis is the
        // one that should run down +Z. (Front vs back is still a coin flip —
        // flip yRotation by 180 per robot if one comes out reversed.)
        Bounds raw = Combine(vehicleRenderers);
        if (raw.size.x > raw.size.z)
        {
            _vehicle.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            raw = Combine(vehicleRenderers);
        }

        // Multiply, never replace — glTF roots carry their own unit scale.
        float scale = (robotBounds.size.y * heightFraction) / Mathf.Max(0.01f, raw.size.y);
        _vehicle.transform.localScale *= scale;

        // Sit it on the same floor line the robot stands on, centred under it.
        Bounds scaled = Combine(vehicleRenderers);
        Vector3 offset = new Vector3(
            robotBounds.center.x - scaled.center.x,
            robotBounds.min.y - scaled.min.y,
            robotBounds.center.z - scaled.center.z);
        _vehicle.transform.position += offset;

        Tint(vehicleRenderers);
        _vehicle.SetActive(false);
    }

    /// <summary>Show the vehicle (or the robot). Safe with no vehicle model.</summary>
    public void SetVehicle(bool vehicle)
    {
        Build();
        if (_vehicle == null)
            return;

        IsShowingVehicle = vehicle;
        _vehicle.SetActive(vehicle);
        if (_robotRenderers != null)
            foreach (var renderer in _robotRenderers)
                if (renderer != null)
                    renderer.enabled = !vehicle;
    }

    /// <summary>
    /// The robot model, searched back to front. RobotFactory.Reskin destroys
    /// the old model and instantiates the new one in the same call, and Unity
    /// defers Destroy to the end of the frame — so for one frame the holder has
    /// two children called "Model" and a forward search finds the dying one.
    /// The replacement is always appended last.
    /// </summary>
    Transform FindNewestModel()
    {
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i);
            if (child.name == "Model")
                return child;
        }
        return null;
    }

    static Bounds Combine(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    /// <summary>Tints copies of the imported materials — never the shared assets.</summary>
    void Tint(Renderer[] renderers)
    {
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                    continue;
                var copy = new Material(materials[i]);
                string property = copy.HasProperty("_BaseColor") ? "_BaseColor"
                    : copy.HasProperty("_Color") ? "_Color" : null;
                if (property != null)
                    copy.SetColor(property, copy.GetColor(property) * Color.Lerp(Color.white, tint, 0.35f));
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
    }
}
