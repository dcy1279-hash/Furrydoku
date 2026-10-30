using UnityEditor;
using UnityEngine;

// Import settings for the victory puppy frame sequence (116 PNG frames).
// Uncompressed they would cost ~220 MB of texture memory; compressed with no
// mipmaps they stay around a quarter of that (the crying puppy keeps mips, see below).
public sealed class VictoryDogFramesImporter : AssetPostprocessor
{
    private const string Folder = "Assets/Resources/VictoryDogV5/";
    private const string FailureFolder = "Assets/Resources/FailureDogV2/"; // crying puppy frames (116)
    private const string AltFolder = "Assets/Resources/VictoryDogV6/";     // second victory dance (112)

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder) && !assetPath.StartsWith(FailureFolder) && !assetPath.StartsWith(AltFolder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        // The crying puppy is drawn well below its texture size; without mips its thin ear/cheek
        // gap aliases and shimmers frame to frame, so it gets trilinear mips (~+33% memory).
        bool failure = assetPath.StartsWith(FailureFolder);
        importer.mipmapEnabled = failure;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = failure ? FilterMode.Trilinear : FilterMode.Bilinear;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
    }
}
