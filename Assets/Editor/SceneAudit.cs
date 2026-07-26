using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode sanity check: opens the arena scene and reports what is actually
/// in it (Meshy models, robots, brains), independent of scene serialization.
///   Unity.exe -batchmode -projectPath <path> -executeMethod SceneAudit.Run
/// </summary>
public static class SceneAudit
{
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GreyboxArena.unity", OpenSceneMode.Single);

            int crates = 0, portals = 0, models = 0, rings = 0, brains = 0, scorers = 0, bubbles = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    switch (t.name)
                    {
                        case "energy-crate": crates++; break;
                        case "spawn-portal": portals++; break;
                        case "Model": models++; break;
                        case "TeamRing": rings++; break;
                    }
                }
                brains += root.GetComponentsInChildren<AIBrain>(true).Length;
                scorers += root.GetComponentsInChildren<TargetDummy>(true).Length;
                bubbles += root.GetComponentsInChildren<ShieldBubble>(true).Length;
            }

            int controllers = 0, lasers = 0, beams = 0, plasmas = 0, rails = 0, scopes = 0, blocks = 0, blockMgrs = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                controllers += root.GetComponentsInChildren<GameModeController>(true).Length;
                blocks += root.GetComponentsInChildren<ArenaBlock>(true).Length;
                blockMgrs += root.GetComponentsInChildren<ArenaBlockManager>(true).Length;
                lasers += root.GetComponentsInChildren<LaserBlaster>(true).Length;
                beams += root.GetComponentsInChildren<PhotonBeam>(true).Length;
                plasmas += root.GetComponentsInChildren<PlasmaLobber>(true).Length;
                rails += root.GetComponentsInChildren<RailZapper>(true).Length;
                scopes += root.GetComponentsInChildren<XRayScope>(true).Length;
            }
            Debug.Log($"[SceneAudit] weapons: laser={lasers} beam={beams} plasma={plasmas} rail={rails} xrayScope={scopes}");
            Debug.Log($"[SceneAudit] dynamic cover: arenaBlocks={blocks} blockManagers={blockMgrs}");

            Debug.Log($"[SceneAudit] crates={crates} portals={portals} robotModels={models} " +
                      $"teamRings={rings} aiBrains={brains} scorers={scorers} shieldBubbles={bubbles} " +
                      $"gameControllers={controllers}");

            var player = GameObject.Find("Player");
            var blaster = player != null ? player.GetComponentInChildren<LaserBlaster>() : null;
            int blasterRenderers = blaster != null ? blaster.GetComponentsInChildren<Renderer>().Length : 0;
            Debug.Log($"[SceneAudit] playerBlaster={(blaster == null ? "NULL" : blaster.gameObject.name)} " +
                      $"blasterRenderers={blasterRenderers} size={BoundsSize(blaster != null ? blaster.gameObject : null)}");

            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Model" || t.name == "energy-crate" || t.name == "spawn-portal")
                        Debug.Log($"[SceneAudit] size check: {t.root.name}/{t.name} = {BoundsSize(t.gameObject)}");

            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneAudit] FAILED: {e}");
            EditorApplication.Exit(1);
        }
    }

    static string BoundsSize(GameObject go)
    {
        if (go == null)
            return "n/a";
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return "no renderers";
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return $"({bounds.size.x:F2} x {bounds.size.y:F2} x {bounds.size.z:F2})m";
    }
}
