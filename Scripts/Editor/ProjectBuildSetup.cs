using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// One-time release settings for the phone build, applied when the editor loads and saved to
// ProjectSettings right away: the real game scene in the build list, the Furrydoku name/ids,
// portrait-only orientation and the Android adaptive icon. Only writes what differs.
[InitializeOnLoad]
public static class ProjectBuildSetup
{
    private const string GameScene = "Assets/Scenes/GameScene.unity";
    private const string Name = "Furrydoku";
    private const string BundleId = "com.furrydoku.game";
    private const string AdaptiveBackground = "Assets/Branding/AppIcon_Adaptive_Background.png";
    private const string AdaptiveForeground = "Assets/Branding/AppIcon_Adaptive_Foreground.png";

    static ProjectBuildSetup() => EditorApplication.delayCall += Apply;

    private static void Apply()
    {
        bool changed = false;

        // Build list: only the real game scene (the template SampleScene is empty).
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) != null)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != GameScene || !scenes[0].enabled)
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true) };
                changed = true;
            }
        }

        if (PlayerSettings.companyName != Name) { PlayerSettings.companyName = Name; changed = true; }
        if (PlayerSettings.productName != Name) { PlayerSettings.productName = Name; changed = true; }
        foreach (NamedBuildTarget target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
        {
            string id = PlayerSettings.GetApplicationIdentifier(target);
            // Only replace the template id or the one Unity derives from company/product name;
            // never overwrite an id someone set on purpose.
            string derived = "com." + PlayerSettings.companyName + "." + PlayerSettings.productName;
            if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany") || id == derived)
            {
                PlayerSettings.SetApplicationIdentifier(target, BundleId);
                changed = true;
            }
        }

        // Portrait only (no auto-rotation into landscape).
        if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            changed = true;
        }
        if (PlayerSettings.allowedAutorotateToLandscapeLeft || PlayerSettings.allowedAutorotateToLandscapeRight ||
            PlayerSettings.allowedAutorotateToPortraitUpsideDown)
        {
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            changed = true;
        }

        changed |= ApplyAdaptiveIcon();

        if (!changed) return;
        AssetDatabase.SaveAssets();
        Debug.Log("ProjectBuildSetup：已设置打包场景 GameScene、名称 Furrydoku、包名 " + BundleId + "、竖屏锁定、安卓自适应图标。");
    }

    // Android launcher icon: the adaptive icon (sky-blue background with the full app icon in the
    // visible centre, transparent foreground). Round/Legacy kinds are obsolete in Unity 6.6 and throw,
    // so they are not touched; Unity derives older-device icons from the default icon. The kind lives
    // in the Android extension assembly (looked up by reflection); without Android support it is skipped.
    private const string AppIcon = "Assets/Branding/AppIcon_1024.png";
    private static bool ApplyAdaptiveIcon()
    {
        var background = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBackground);
        var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveForeground);
        var app = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIcon);
        if (background == null || foreground == null || app == null) return false;
        bool changed = false;
        try { changed |= SetAndroidIcons("Adaptive", background, foreground); }
        catch (System.Exception e) { Debug.LogWarning("ProjectBuildSetup：安卓自适应图标未设置：" + e.Message); }
        return changed;
    }

    private static bool SetAndroidIcons(string kindName, Texture2D first, Texture2D second)
    {
        System.Type kindType = System.Type.GetType("UnityEditor.Android.AndroidPlatformIconKind, UnityEditor.Android.Extensions");
        if (kindType == null) return false;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        var kind = (kindType.GetField(kindName, flags)?.GetValue(null) ??
                    kindType.GetProperty(kindName, flags)?.GetValue(null)) as PlatformIconKind;
        if (kind == null) return false;
        PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        if (icons == null || icons.Length == 0) return false;
        Texture2D[] textures = second != null ? new[] { first, second } : new[] { first };
        if (icons.All(i => Enumerable.Range(0, textures.Length).All(k => i.GetTexture(k) == textures[k]))) return false;
        foreach (PlatformIcon icon in icons) icon.SetTextures(textures);
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        return true;
    }
}
