#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Renders the menu's robot art: every mode diorama plus the title-screen hero
/// line-up, straight out of the game's own rigs, to PNGs that
/// Tools/menucomposite.py then lays over painted backgrounds.
///
/// Why this lives in the runtime assembly rather than under Assets/Editor:
/// the rigs are built by runtime-only code (RobotFactory, TeamPaint), so this
/// has to happen in play mode, and an [InitializeOnLoad] hook does not
/// reliably see play mode on the domain reload that starts it — the earlier
/// version of this tool sat through a whole batch run without firing. A
/// [RuntimeInitializeOnLoadMethod] runs when the GAME starts, which is exactly
/// the moment we want, and it needs no EditorPrefs handshake to survive the
/// reloads.
///
///     Unity -batchmode -projectPath . -executeMethod MenuArtForge.Batch -forgeMenuArt
///
/// Each rig is rendered TWICE, once on black and once on white. URP does not
/// promise a meaningful alpha channel in the colour target, so the cutout is
/// derived from the pair instead: where the two renders agree the pixel is
/// opaque, and where they differ by the full black-to-white swing it is empty.
///
/// Edges are supersampled rather than multisampled: every render goes through
/// one of two shared targets at SUPERSAMPLE times the output size and is
/// resized down by the compositor. An MSAA target allocated per rig crashed
/// the D3D12 device outright, and ReadPixels off a multisampled target is a
/// resolve you do not control. Pair this with -force-d3d11 in batch.
/// </summary>
public static class MenuArtForge
{
    public const string Arg = "-forgeMenuArt";
    public const string OutDir = "Tools/menu_render";
    public const int CardW = 1280, CardH = 720;
    public const int HeroW = 1920, HeroH = 1080;
    public const int Supersample = 2;

    const string ScenePath = "Assets/Scenes/GreyboxArena.unity";

    /// <summary>
    /// Batch entry: open the arena and press Play. The render happens in
    /// <see cref="MenuArtForgeRunner"/> once the game is actually running.
    /// </summary>
    public static void Batch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        UnityEditor.EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Arg) < 0)
            return;
        var go = new GameObject("MenuArtForge");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<MenuArtForgeRunner>();
    }
}

class MenuArtForgeRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        int failed = 0;
        try
        {
            Directory.CreateDirectory(MenuArtForge.OutDir);
        }
        catch (Exception e)
        {
            Debug.LogError($"MenuArtForge: cannot create {MenuArtForge.OutDir}: {e.Message}");
            UnityEditor.EditorApplication.Exit(1);
            yield break;
        }

        var controller = FindFirstObjectByType<GameModeController>();
        var roster = controller != null ? controller.GetComponent<RobotRoster>() : null;
        if (roster == null || !roster.HasRobots)
        {
            Debug.LogError("MenuArtForge: no RobotRoster in the scene — nothing to cast.");
            UnityEditor.EditorApplication.Exit(1);
            yield break;
        }

        var set = MenuIconRigs.Build(transform, roster);

        // Two targets for the whole run, reused. Allocating one per rig is
        // what took the graphics device down.
        var cardRt = NewTarget(MenuArtForge.CardW, MenuArtForge.CardH);
        var heroRt = NewTarget(MenuArtForge.HeroW, MenuArtForge.HeroH);

        // Team repaints are blitted over the following frames, and a rig
        // rendered before they land comes out in primer grey.
        for (int i = 0; i < 90; i++)
            yield return null;

        for (int i = 0; i < set.cameras.Length; i++)
        {
            var cam = set.cameras[i];
            if (cam == null)
            {
                failed++;
                continue;
            }
            // The rig cameras were framed for a square 320px tile and crop the
            // robots' heads at card aspect. Widening the (vertical) FOV buys
            // headroom without disturbing each diorama's tuned viewpoint.
            cam.fieldOfView = Mathf.Min(cam.fieldOfView * 1.32f, 75f);
            HideGround(set.root.transform);
            yield return Shoot(cam, $"{(MenuIcon)i}", cardRt);
        }

        // The line-ups ride well clear of the icon rigs, which are spaced 40
        // apart from the root.
        // Walker prefab, yaw 0. The fighter prefab's rest pose is a side-on
        // fighting stance -- rendering all four yaws of both settled it.
        var heroRig = new GameObject("HeroRig").transform;
        heroRig.SetParent(set.root.transform, false);
        heroRig.localPosition = new Vector3(-80f, 0f, 0f);
        var heroCam = MenuIconRigs.BuildHeroLineup(heroRig, roster, heroRt, 0f, true);
        for (int i = 0; i < 60; i++)
            yield return null;
        if (heroCam != null)
            yield return Shoot(heroCam, "HeroLineup", heroRt);
        else
            failed++;

        Debug.Log($"MenuArtForge: wrote {set.cameras.Length + 1 - failed} renders to "
                  + $"{MenuArtForge.OutDir} ({failed} failed)");
        yield return null;
        UnityEditor.EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    /// <summary>
    /// The rigs' own ground: the fight disc, the strategy table, the tank
    /// road, the tower-defense canyon. All of it is grey primitive geometry
    /// that read fine as a self-contained diorama and reads as a slab of
    /// nothing once there is a painted floor behind it. The plate is the
    /// ground now; the compositor puts a contact shadow back under whatever
    /// is left standing.
    /// </summary>
    static readonly string[] GroundMaterials =
    {
        "MenuIcon_Floor", "MenuIcon_Table",
        "MenuIcon_TankDeck", "MenuIcon_TankBerm", "MenuIcon_TankStripe",
        "MenuIcon_Sand", "MenuIcon_Rock",
    };

    static void HideGround(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (material == null)
                continue;
            foreach (var ground in GroundMaterials)
                if (material.name.StartsWith(ground))
                {
                    renderer.enabled = false;
                    break;
                }
        }
    }

    static RenderTexture NewTarget(int width, int height)
    {
        int s = MenuArtForge.Supersample;
        var rt = new RenderTexture(width * s, height * s, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
        };
        rt.Create();
        return rt;
    }

    /// <summary>One rig, twice: on black and on white, into a shared target.</summary>
    static IEnumerator Shoot(Camera cam, string name, RenderTexture rt)
    {
        var previousTarget = cam.targetTexture;
        var previousClear = cam.clearFlags;
        var previousColor = cam.backgroundColor;
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;

        Write(cam, rt, Color.black, $"{MenuArtForge.OutDir}/{name}_k.png");
        yield return null;
        Write(cam, rt, Color.white, $"{MenuArtForge.OutDir}/{name}_w.png");
        yield return null;

        cam.targetTexture = previousTarget;
        cam.clearFlags = previousClear;
        cam.backgroundColor = previousColor;
    }

    static void Write(Camera cam, RenderTexture rt, Color backdrop, string path)
    {
        cam.backgroundColor = backdrop;
        cam.Render();

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes(path, shot.EncodeToPNG());
        Destroy(shot);
    }
}
#endif
