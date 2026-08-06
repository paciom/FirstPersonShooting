using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reports which weapons have a model and which are still wearing the fallback
/// silhouette.
///
/// WHY THIS EXISTS RATHER THAN TRUST. WeaponArt resolves a model by DERIVING its
/// file name from the class name, and a miss is indistinguishable from a weapon
/// that was never generated: it quietly falls back and the game keeps running.
/// That is the right behaviour at runtime and a terrible one to debug — a single
/// typo in a downloaded file name, or a GLB whose importer produced something
/// other than a GameObject, costs one weapon its model and says nothing.
///
/// Menu rather than a test because it has to run against the real Resources
/// folder with the real importers, which is the thing being checked.
/// </summary>
public static class WeaponArtAudit
{
    [MenuItem("Photon Arena/Audit Weapon Art")]
    public static void Audit()
    {
        var missing = new StringBuilder();
        int found = 0, total = 0;

        foreach (var tab in WeaponCatalog.Tabs)
        {
            var absent = new StringBuilder();
            foreach (var type in tab.types)
            {
                total++;
                if (WeaponArt.ModelForType(type) != null)
                {
                    found++;
                    continue;
                }
                absent.Append(absent.Length == 0 ? "" : ", ").Append(WeaponArt.KeyFor(type));
            }
            if (absent.Length > 0)
                missing.Append($"\n  {tab.name}: {absent}");
        }

        string where = "Assets/Resources/Weapons/<key>.glb";
        if (missing.Length == 0)
        {
            Debug.Log($"[WeaponArt] all {total} weapons have a model.");
            return;
        }

        // A warning, not an error: the fallback is a legitimate state while the
        // pipeline is still running, and this is meant to be run mid-generation.
        Debug.LogWarning(
            $"[WeaponArt] {found}/{total} weapons have a model. Missing (expected at {where}):{missing}");
    }
}
