using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The corner portraits: each fighter's actual roster robot, team-painted,
/// slowly turning under its own point light on a tiny stage parked far below
/// the fight, filmed by a 128px RenderTexture camera. Same studio trick
/// CommanderPortraits established — nothing else stands within the camera's
/// short far plane, and the local light exists because no stage light is
/// aimed down there. Rigs live under this component, so the whole studio is
/// struck with the Brawl session — except the RenderTextures, which Unity
/// will not destroy with a hierarchy and are released in OnDestroy.
/// </summary>
public class BrawlPortraits : MonoBehaviour
{
    const float StageY = -220f;
    const float StageX = -420f;
    const float Spacing = 16f;

    // NOT readonly: survives a mid-Play recompile as a serialized field, so
    // the release list keeps its textures across the reload.
    List<RenderTexture> _textures = new List<RenderTexture>();
    int _slot;

    public static BrawlPortraits Build(Transform parent)
    {
        var go = new GameObject("BrawlPortraits");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(StageX, StageY, 0f);
        return go.AddComponent<BrawlPortraits>();
    }

    class Spinner : MonoBehaviour
    {
        void Update() => transform.Rotate(0f, 36f * Time.unscaledDeltaTime, 0f);
    }

    /// <summary>A live portrait texture of the robot — null without a model.</summary>
    public Texture Add(GameObject modelPrefab, Color tint)
    {
        if (modelPrefab == null)
            return null;

        var root = new GameObject("Portrait");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(_slot++ * Spacing, 0f, 0f);

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        // Face the camera first; the spin only keeps the portrait alive.
        body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        RobotFactory.InstantiateNormalized(modelPrefab, body.transform, tint);
        body.AddComponent<Spinner>();

        var lightGo = new GameObject("Key");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(1.3f, 2.2f, -1.6f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.intensity = 2.4f;
        light.range = 6.5f;

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.5f, -2.5f);
        camGo.transform.LookAt(root.transform.position + Vector3.up * 0.7f);
        var cam = camGo.AddComponent<Camera>();
        // NOT tagged MainCamera — Camera.main must keep meaning the fight view.
        cam.fieldOfView = 36f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 12f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 1f);
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;

        // 256, not 128: this texture is no longer only an 84px corner badge —
        // the K.O. card blows the winner's face up to 268px, and the upscale
        // from 128 was the one soft thing on a picture kids pass around.
        var texture = new RenderTexture(256, 256, 16);
        cam.targetTexture = texture;
        _textures.Add(texture);
        return texture;
    }

    void OnDestroy()
    {
        foreach (var texture in _textures)
        {
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
        _textures.Clear();
    }
}
