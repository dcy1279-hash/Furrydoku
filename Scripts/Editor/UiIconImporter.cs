using UnityEditor;
using UnityEngine;

// High-resolution UI icons (rendered from the approved SVGs at 512 px). Mipmaps +
// trilinear keep them smooth when the HUD/settings draw them at ~60-120 px.
public sealed class UiIconImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Resources/UiIcons/";
    private const string BrandingFolder = "Assets/Resources/Branding/"; // game logo, up to 2048 px

    private void OnPreprocessTexture()
    {
        bool branding = assetPath.StartsWith(BrandingFolder);
        if (!assetPath.StartsWith(Folder) && !branding) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = branding ? 2048 : 512;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
    }
}
