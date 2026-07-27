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

    [Tooltip("Transformation stages, robot first and vehicle last. When present " +
             "these replace vehiclePrefab entirely: the fold plays through them " +
             "as stop motion and the last one IS the vehicle form.")]
    public GameObject[] transformStages;

    [Tooltip("Transform whose 'Model' child is the robot; defaults to this one.")]
    public Transform holder;

    [Tooltip("Team tint, applied the same way RobotFactory tints robot models.")]
    public Color tint = Color.white;

    [Tooltip("Vehicle height as a fraction of the standing robot's height.")]
    [Range(0.2f, 1.5f)] public float heightFraction = 0.62f;

    [Tooltip("Extra yaw applied to generated STAGES only. They come out of the " +
             "image-to-3D pipeline nose-down -Z, which drives them backwards; " +
             "the separately generated vehiclePrefab models do not and are left " +
             "alone. Flip by 180 if a robot's tank still reverses.")]
    public float stageYawOffset = 180f;

    public bool HasVehicle =>
        vehiclePrefab != null || (transformStages != null && transformStages.Length > 1);

    /// <summary>Currently showing the vehicle rather than the robot?</summary>
    public bool IsShowingVehicle { get; private set; }

    GameObject _vehicle;
    Renderer[] _robotRenderers;

    /// <summary>Instantiated stages; index 0 is null because stage one is the live robot.</summary>
    GameObject[] _stages;

    /// <summary>Number of stop-motion steps, or 0 when this robot has none.</summary>
    public int StageCount => _stages != null ? _stages.Length : 0;

    /// <summary>True when the fold should play as stop motion rather than one swap.</summary>
    public bool HasStages { get { Build(); return StageCount > 1; } }
    bool _built;

    void Start()
    {
        Build();
    }

    /// <summary>
    /// Point at a robot's transformation stages. Takes precedence over
    /// <see cref="vehiclePrefab"/>, because a robot that has real stages should
    /// turn into the thing at the end of them rather than into a separately
    /// generated vehicle that never appears in its own transformation.
    /// </summary>
    public void SetStages(GameObject[] stages, Color teamTint)
    {
        ClearBuilt();
        transformStages = stages;
        tint = teamTint;
        Build();
    }

    /// <summary>
    /// Point at a different vehicle model — the runtime robot swap on the
    /// select screen changes which robot is worn, and with it which vehicle it
    /// should turn into.
    /// </summary>
    public void SetVehiclePrefab(GameObject prefab, Color teamTint)
    {
        ClearBuilt();
        vehiclePrefab = prefab;
        tint = teamTint;
        Build();
    }

    void ClearBuilt()
    {
        if (_vehicle != null)
            Destroy(_vehicle);
        _vehicle = null;
        if (_stages != null)
            foreach (var stage in _stages)
                if (stage != null)
                    Destroy(stage);
        _stages = null;
        _built = false;
        IsShowingVehicle = false;
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

        if (_robotRenderers.Length == 0)
            return;

        Bounds robotBounds = Combine(_robotRenderers);

        if (transformStages != null && transformStages.Length > 1)
        {
            BuildStages(robotBounds);
            return;
        }

        if (vehiclePrefab == null)
            return;

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

        // (Front vs back is still a coin flip — flip yRotation by 180 per robot
        // if one comes out reversed.)
        FitToRobot(_vehicle, vehicleRenderers, robotBounds, 0f);
        Tint(vehicleRenderers);
        _vehicle.SetActive(false);
    }

    /// <summary>
    /// Instantiates every stage after the first, fitted the same way a single
    /// vehicle model is.
    ///
    /// Stage one is skipped on purpose: it is the live, animated robot already
    /// standing there. Rebuilding it as a static mesh would drop the walk cycle
    /// and swap a rigged robot for a frozen copy of itself on the first frame
    /// of every transformation.
    /// </summary>
    void BuildStages(Bounds robotBounds)
    {
        _stages = new GameObject[transformStages.Length];
        for (int i = 1; i < transformStages.Length; i++)
        {
            if (transformStages[i] == null)
                continue;

            var stage = Instantiate(transformStages[i], holder);
            stage.name = $"Stage{i + 1}";
            stage.transform.localPosition = Vector3.zero;
            stage.transform.localRotation = Quaternion.identity;
            stage.transform.localScale = Vector3.one;

            var renderers = stage.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Destroy(stage);
                continue;
            }

            FitToRobot(stage, renderers, robotBounds, stageYawOffset);
            Tint(renderers);
            stage.SetActive(false);
            _stages[i] = stage;
        }

        // The last stage is the vehicle, so everything that already asks about
        // _vehicle keeps working without knowing stages exist.
        _vehicle = _stages[_stages.Length - 1];
    }

    /// <summary>Orient, scale and ground a generated model against the robot wearing it.</summary>
    void FitToRobot(GameObject instance, Renderer[] renderers, Bounds robotBounds,
                    float extraYaw)
    {
        // Generated models don't agree on which way is forward. A ground
        // vehicle is longer than it is wide, so the long horizontal axis is the
        // one that should run down +Z; extraYaw then turns it to face the way
        // the robot does.
        //
        // Both applied in ONE rotation before anything is measured: rotating
        // after the fit would move the mesh off the centring and grounding
        // solved for its old orientation.
        Bounds raw = Combine(renderers);
        float yaw = (raw.size.x > raw.size.z ? 90f : 0f) + extraYaw;
        if (!Mathf.Approximately(yaw, 0f))
        {
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            raw = Combine(renderers);
        }

        // Multiply, never replace — glTF roots carry their own unit scale.
        float scale = (robotBounds.size.y * heightFraction) / Mathf.Max(0.01f, raw.size.y);
        instance.transform.localScale *= scale;

        // Sit it on the same floor line the robot stands on, centred under it.
        Bounds scaled = Combine(renderers);
        instance.transform.position += new Vector3(
            robotBounds.center.x - scaled.center.x,
            robotBounds.min.y - scaled.min.y,
            robotBounds.center.z - scaled.center.z);
    }

    /// <summary>
    /// Show one step of the transformation. Index 0 is the live robot; anything
    /// higher is a generated stage. Out-of-range clamps, so callers can drive
    /// this straight off a normalised fold time.
    /// </summary>
    public void ShowStage(int index)
    {
        Build();
        if (_stages == null)
            return;

        index = Mathf.Clamp(index, 0, _stages.Length - 1);
        IsShowingVehicle = index >= _stages.Length - 1;

        if (_robotRenderers != null)
            foreach (var renderer in _robotRenderers)
                if (renderer != null)
                    renderer.enabled = index == 0;

        for (int i = 1; i < _stages.Length; i++)
            if (_stages[i] != null)
                _stages[i].SetActive(i == index);
    }

    /// <summary>Show the vehicle (or the robot). Safe with no vehicle model.</summary>
    public void SetVehicle(bool vehicle)
    {
        Build();

        // Stop-motion robots jump straight to either end of the sequence; the
        // steps in between belong to the fold, which drives ShowStage itself.
        if (_stages != null)
        {
            ShowStage(vehicle ? _stages.Length - 1 : 0);
            return;
        }

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
