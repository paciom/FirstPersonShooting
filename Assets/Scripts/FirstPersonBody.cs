using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps the local player's own robot — and the tank it folds into — out of
/// their own camera.
///
/// The player used to be a bare capsule, so first person saw nothing but the
/// viewmodel. Now that they wear the same rig the bots do, the camera sits at
/// head height INSIDE a 1.6m model: some robots put a cowl or a shoulder plate
/// straight across the view, and vehicle form is worse — the hull is fitted
/// around the robot's bounds, so the driver ends up looking at the inside of
/// their own tank. Which robot is affected depends on the model, which makes it
/// look like a bug in one robot rather than a rule about all of them.
///
/// SHADOWS ONLY rather than disabling the renderers, for two reasons. The
/// shadow is worth keeping: it is the only way to tell, in first person, that
/// you are currently a tank. And it is the only setting that STAYS PUT —
/// VehicleSkin drives its stage swaps through Renderer.enabled and would switch
/// anything turned off there straight back on, every fold, forever.
///
/// Re-applied rather than set once, because the set of renderers under Body
/// keeps changing: a robot swap rebuilds the model, VehicleSkin instantiates
/// eight stages lazily on the first fold, and TransformMode grows wheels and
/// thrusters the first time one lands.
/// </summary>
public class FirstPersonBody : MonoBehaviour
{
    /// <summary>
    /// The one thing under Body that is feedback rather than robot: the hit
    /// flash belongs to the player, so it stays visible.
    /// </summary>
    const string ShieldBubbleName = "ShieldBubble";

    /// <summary>How often the quiet rescan runs. Nothing changes between folds.</summary>
    const float IdleInterval = 0.4f;

    [Tooltip("The rig holding the model, the vehicle stages and the wheels; found automatically.")]
    public Transform body;

    readonly List<Renderer> _renderers = new List<Renderer>();
    TransformMode _vehicle;
    float _nextScan;
    float _urgentUntil;

    void Awake()
    {
        if (body == null)
            body = transform.Find("Body");
        _vehicle = GetComponent<TransformMode>();
    }

    void OnEnable()
    {
        if (_vehicle != null)
            _vehicle.OnFoldStarted += HandleFold;
        Refresh();
    }

    void OnDisable()
    {
        if (_vehicle != null)
            _vehicle.OnFoldStarted -= HandleFold;
    }

    /// <summary>
    /// A fold is the one moment new geometry appears mid-frame — stages are
    /// built on the first one and swapped on every one — so the rescan goes to
    /// every frame for as long as it runs. A 0.4s gap here would be 0.4s of
    /// looking at the inside of your own tank.
    /// </summary>
    void HandleFold(bool toVehicle)
    {
        _urgentUntil = Time.time + TransformMode.FoldSeconds + 0.25f;
    }

    void LateUpdate()
    {
        if (Time.time < _urgentUntil || Time.time >= _nextScan)
            Refresh();
    }

    /// <summary>
    /// Hide everything under Body from the camera, keeping its shadow. Cheap
    /// enough to call whenever the player's model is rebuilt.
    /// </summary>
    public void Refresh()
    {
        _nextScan = Time.time + IdleInterval;
        if (body == null)
            return;

        // The List overload, so a scan running every frame through a fold does
        // not allocate an array every frame.
        body.GetComponentsInChildren(true, _renderers);
        foreach (var renderer in _renderers)
        {
            if (renderer == null || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                continue;
            if (IsShieldBubble(renderer.transform))
                continue;
            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    bool IsShieldBubble(Transform child)
    {
        for (var cursor = child; cursor != null && cursor != body; cursor = cursor.parent)
            if (cursor.name == ShieldBubbleName)
                return true;
        return false;
    }
}
