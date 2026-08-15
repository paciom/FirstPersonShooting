#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for the adventure stills (Assets/Resources/Adventures/&lt;story&gt;/).
///
/// Same reason MenuArtImporter exists: a Default-type texture imports with
/// npotScale = ToNearest, which rescales a 1280x720 painting to the nearest
/// power of two on each axis independently and hands back visibly squashed
/// art, with nothing anywhere reporting a problem. Pinning the settings here
/// rather than in .meta files means regenerating the art
/// (Tools/adventure_compose.py) can never reintroduce it.
/// </summary>
class AdventureArtImporter : AssetPostprocessor
{
    const string Folder = "/Resources/Adventures/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains(Folder))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Compressed;

        // A still is drawn 1320 wide on the reference canvas and there are
        // three dozen of them per adventure, which is exactly the shape of
        // problem that made the first WebGL build 402 MB.
        importer.maxTextureSize = 1024;
    }
}
#endif
