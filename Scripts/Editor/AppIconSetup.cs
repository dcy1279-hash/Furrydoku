using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Makes Assets/Branding/AppIcon_1024.png the project's default app icon (all platforms derive
// their sizes from it). Runs once per editor load and only writes when the icon differs.
[InitializeOnLoad]
public static class AppIconSetup
{
    private const string IconPath = "Assets/Branding/AppIcon_1024.png";

    static AppIconSetup() => EditorApplication.delayCall += Apply;

    private static void Apply()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null) return;
        Texture2D[] current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
        if (current != null && current.Length > 0 && current[0] == icon) return;
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        AssetDatabase.SaveAssets(); // write ProjectSettings.asset now, not only on the next manual save
        Debug.Log("AppIconSetup：已把 " + IconPath + " 设为默认 App 图标。");
    }
}
