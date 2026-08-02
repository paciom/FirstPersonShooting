#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for the painted menu art (Assets/Resources/Menu).
///
/// These exist because the default texture importer would quietly ruin them.
/// A Default-type texture imports with npotScale = ToNearest, which rescales a
/// 1280x720 painting to the nearest power of two on each axis independently —
/// the art comes back visibly squashed, and nothing in the scene reports it.
/// Locking the settings here rather than in .meta files means regenerating the
/// art (Tools/menuart.py) cannot reintroduce the problem.
/// </summary>
class MenuArtImporter : AssetPostprocessor
{
    const string Folder = "/Resources/Menu/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains(Folder))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        // Cards are drawn at 224x126 on the reference canvas, so mips are what
        // stop them shimmering when the window is resized.
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Compressed;

        // The key art covers the whole screen; the cards never exceed a tenth
        // of its width, and a WebGL build has already been bitten once by
        // shipping menu textures at full size.
        bool fullscreen = System.IO.Path.GetFileNameWithoutExtension(assetPath)
            .StartsWith("keyart", System.StringComparison.OrdinalIgnoreCase);
        importer.maxTextureSize = fullscreen ? 2048 : 1024;
    }
}
#endif
