using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedurally generates the VFX sprite textures (glow, spark, shockwave ring,
/// hex energy grid, smoke puff) into Assets/Resources/VFX so VfxUtil can load
/// them at runtime. Regenerate anytime via Photon Arena > Generate VFX Sprites.
/// </summary>
public static class SpriteForge
{
    const int Size = 256;
    const string Dir = "Assets/Resources/VFX";

    [MenuItem("Photon Arena/Generate VFX Sprites")]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(Dir);

        Generate("glow", (x, y) =>
        {
            float r = Radius(x, y);
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
            return new Color(1f, 1f, 1f, a);
        });

        Generate("spark", (x, y) =>
        {
            float r = Radius(x, y);
            float theta = Mathf.Atan2(y - 0.5f, x - 0.5f);
            float star = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * theta)), 14f)
                       + Mathf.Pow(Mathf.Abs(Mathf.Sin(2f * theta)), 14f);
            float core = Mathf.Pow(Mathf.Clamp01(1f - r * 2.2f), 2f);
            float rays = Mathf.Clamp01(star) * Mathf.Pow(Mathf.Clamp01(1f - r), 1.5f);
            float a = Mathf.Clamp01(core + rays);
            return new Color(1f, 1f, 1f, a);
        });

        Generate("ring", (x, y) =>
        {
            float r = Radius(x, y);
            float band = Mathf.Exp(-Mathf.Pow((r - 0.40f) / 0.045f, 2f));
            return new Color(1f, 1f, 1f, band);
        });

        Generate("hex", (x, y) =>
        {
            // Three line families at 0/60/120 degrees form an energy lattice.
            float a = 0f;
            for (int k = 0; k < 3; k++)
            {
                float angle = k * Mathf.PI / 3f;
                float d = x * Mathf.Cos(angle) + y * Mathf.Sin(angle);
                float line = Mathf.Pow(Mathf.Abs(Mathf.Sin(d * Mathf.PI * 6f)), 24f);
                a = Mathf.Max(a, line);
            }
            return new Color(1f, 1f, 1f, a);
        }, wrap: TextureWrapMode.Repeat);

        Generate("puff", (x, y) =>
        {
            float r = Radius(x, y);
            float noise = Mathf.PerlinNoise(x * 5f + 13.7f, y * 5f + 4.2f);
            float a = Mathf.Clamp01(noise * 0.9f + 0.1f) * Mathf.Pow(Mathf.Clamp01(1f - r * 1.15f), 1.6f);
            return new Color(1f, 1f, 1f, a);
        });

        AssetDatabase.Refresh();
        Debug.Log("[SpriteForge] VFX sprites generated.");
    }

    static float Radius(float x, float y) =>
        Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) * 2f;

    static void Generate(string name, Func<float, float, Color> pixel, TextureWrapMode wrap = TextureWrapMode.Clamp)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        var pixels = new Color[Size * Size];
        for (int py = 0; py < Size; py++)
        for (int px = 0; px < Size; px++)
            pixels[py * Size + px] = pixel((px + 0.5f) / Size, (py + 0.5f) / Size);
        tex.SetPixels(pixels);
        tex.Apply();

        string path = $"{Dir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = wrap;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }
}
