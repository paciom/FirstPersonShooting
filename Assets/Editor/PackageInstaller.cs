using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>
/// Batch-mode bootstrap: installs all packages Photon Arena needs in a single
/// Package Manager transaction (one resolve, no mid-install domain reloads).
/// Run with:
///   Unity.exe -batchmode -projectPath <path> -executeMethod PackageInstaller.Install -logFile <log>
/// The method blocks until done and exits the editor itself (do NOT pass -quit).
/// </summary>
public static class PackageInstaller
{
    static readonly string[] Packages =
    {
        "com.unity.render-pipelines.universal",
        "com.unity.inputsystem",
        "com.unity.cinemachine",
        "com.unity.ai.navigation",
        "com.unity.recorder",
        "com.unity.ugui",
    };

    public static void Install()
    {
        Debug.Log("[PackageInstaller] Starting package installation...");
        ApplyPlayerSettings();

        var request = Client.AddAndRemove(Packages, null);
        while (!request.IsCompleted)
            Thread.Sleep(250);

        if (request.Status == StatusCode.Success)
        {
            foreach (var pkg in request.Result)
                Debug.Log($"[PackageInstaller] Resolved {pkg.name}@{pkg.version}");
            Debug.Log("[PackageInstaller] All packages installed successfully.");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[PackageInstaller] FAILED: {request.Error?.message}");
            EditorApplication.Exit(1);
        }
    }

    static void ApplyPlayerSettings()
    {
        PlayerSettings.productName = "Photon Arena";
        PlayerSettings.companyName = "PhotonArena";

        // activeInputHandler has no public API; 2 = "Both" so legacy Input keeps
        // working while the Input System package is available for later phases.
        var playerSettings = new SerializedObject(
            Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
        var prop = playerSettings.FindProperty("activeInputHandler");
        if (prop != null)
        {
            prop.intValue = 2;
            playerSettings.ApplyModifiedProperties();
            Debug.Log("[PackageInstaller] activeInputHandler set to Both.");
        }
        else
        {
            Debug.LogWarning("[PackageInstaller] Could not find activeInputHandler property.");
        }
    }
}
