using UnityEngine;

// Presentation-only experiment. No gameplay, save-format or camera changes.
[CreateAssetMenu(menuName = "PetPuzzle/Puppy Avatar Presentation")]
public sealed class PuppyAvatarPresentation : ScriptableObject
{
    [Header("试验版3D狗头：取消勾选即恢复原2D头像")]
    public bool useExperimental3D = true;
    private static PuppyAvatarPresentation settings;
    public static bool Use3D
    {
        get
        {
            if (settings == null) settings = Resources.Load<PuppyAvatarPresentation>("PuppyHead3DV1/Presentation");
            return settings != null && settings.useExperimental3D;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCachedSettings() { settings = null; }
}
