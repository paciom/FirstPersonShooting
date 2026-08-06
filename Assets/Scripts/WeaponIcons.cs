using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pictures of weapons for the ARMS rack, rendered from the props themselves.
///
/// WHY RENDER RATHER THAN SHIP PNGs. The alternative is an offline capture pass
/// writing sixty files, which is how the menu icons are made — right for those,
/// because they are composed artwork. These are not: they are the weapon, from
/// an angle, and the moment a model is regenerated the shipped PNG is a picture
/// of a gun that no longer exists. Rendering keeps the rack honest for free, and
/// gives every weapon a picture from the first frame — including the ones still
/// wearing <see cref="WeaponArt"/>'s fallback silhouette.
///
/// Rendered ONCE per weapon, not per frame. The camera is disabled and driven by
/// hand: place the prop, Render(), keep the texture, destroy the prop. A rack of
/// sixty 128px icons costs about 4MB and no frame time at all after the first
/// look. The whole rig goes when the rack is torn down.
///
/// Parked far below the arena, below TransformCast's form rig at -300 and the
/// select screen's preview rigs above it, with its own short-range lights: the
/// arena's key light travels toward +Z and would leave every icon lit from
/// behind.
/// </summary>
public class WeaponIcons : MonoBehaviour
{
    /// <summary>Side of one icon, in pixels.</summary>
    const int Size = 128;

    /// <summary>Below everything else that parks a rig under the floor.</summary>
    const float RigDepth = -420f;

    /// <summary>
    /// Backdrop behind the prop. Matches the select screen's, so a weapon and a
    /// robot read against the same grey rather than two different darks.
    /// </summary>
    static readonly Color Backdrop = new Color(0.16f, 0.17f, 0.19f, 1f);

    static WeaponIcons _instance;

    readonly Dictionary<System.Type, RenderTexture> _icons =
        new Dictionary<System.Type, RenderTexture>();

    Camera _camera;
    Transform _stage;

    /// <summary>
    /// The picture for a weapon, rendering it on first ask. Null only for a null
    /// weapon — every real one has at least the fallback silhouette.
    /// </summary>
    public static Texture IconFor(Weapon weapon)
    {
        if (weapon == null)
            return null;
        return Ensure().Render(weapon);
    }

    /// <summary>
    /// Give back every icon and the rig that drew them. Called when the rack is
    /// torn down — render textures are native memory and do not go with a scene
    /// change on their own.
    /// </summary>
    public static void Release()
    {
        if (_instance != null)
            Destroy(_instance.gameObject);
        _instance = null;
    }

    static WeaponIcons Ensure()
    {
        if (_instance == null)
        {
            var go = new GameObject("WeaponIcons");
            _instance = go.AddComponent<WeaponIcons>();
            _instance.Build();
        }
        return _instance;
    }

    void OnDestroy()
    {
        foreach (var texture in _icons.Values)
            if (texture != null)
                texture.Release();
        _icons.Clear();
        if (_instance == this)
            _instance = null;
    }

    void Build()
    {
        transform.position = new Vector3(0f, RigDepth, 0f);

        var stageGo = new GameObject("Stage");
        stageGo.transform.SetParent(transform, false);
        _stage = stageGo.transform;
        // Three-quarter view: a gun photographed square-on down the barrel is a
        // circle, and square-on from the side is a featureless bar. Turned and
        // tipped, the silhouette shows both the length and the shape.
        _stage.localRotation = Quaternion.Euler(12f, 34f, 0f);

        var camGo = new GameObject("IconCam");
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.10f, -0.85f);
        camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        _camera = camGo.AddComponent<Camera>();
        _camera.fieldOfView = 40f;
        _camera.nearClipPlane = 0.05f;
        _camera.farClipPlane = 4f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Backdrop;
        // Driven by hand, one Render per icon — an enabled camera would redraw
        // an unchanging picture every frame for the life of the match.
        _camera.enabled = false;
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;

        // Same three-point idea as the robot previews, at weapon scale: the
        // props are ~0.5m rather than 1.6m, so the lights sit close and the
        // intensities come down with the square of the distance.
        AddLight(new Vector3(0.55f, 0.60f, -0.55f), new Color(1f, 0.97f, 0.90f), 1.6f);
        AddLight(new Vector3(-0.60f, 0.15f, -0.45f), new Color(0.55f, 0.72f, 1f), 0.7f);
        AddLight(new Vector3(0f, 0.40f, 0.70f), new Color(0.80f, 0.90f, 1f), 0.9f);
    }

    void AddLight(Vector3 localPosition, Color color, float intensity)
    {
        var go = new GameObject("IconLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 3f;
        light.shadows = LightShadows.None;
    }

    RenderTexture Render(Weapon weapon)
    {
        var type = weapon.GetType();
        if (_icons.TryGetValue(type, out var cached) && cached != null)
            return cached;

        var texture = new RenderTexture(Size, Size, 16);
        var prop = WeaponArt.BuildProp(weapon, _stage);

        _camera.targetTexture = texture;
        _camera.Render();
        _camera.targetTexture = null;

        // Immediate, not deferred: the next icon renders on this same frame when
        // a whole tab is opened at once, and a prop that lives until end-of-frame
        // would photobomb the one after it.
        DestroyImmediate(prop);

        _icons[type] = texture;
        return texture;
    }
}
