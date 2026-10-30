using UnityEngine;

// Shared, registered transparent expression sprites. Only these runtime
// sprites are owned; resource textures are borrowed and never destroyed here.
public static class PuppyAvatarAssets
{
    private static Sprite normal, wink, left, right, blink;
    private static bool warned;
    public static bool TryGet(out Sprite face, out Sprite winkFace, out Sprite lookLeft, out Sprite lookRight)
    {
        if (normal == null) normal = Load("Normal");
        face = normal; winkFace = lookLeft = lookRight = normal;
        if (normal == null)
        {
            if (!warned) Debug.LogWarning("PuppyAvatarAssets：白狗头像尚未导入，保留原动物显示。");
            warned = true; return false;
        }
        if (wink == null) wink = Load("Wink");
        if (left == null) left = LoadPose("TurnLeft") ?? Load("LookLeft");
        if (right == null) right = LoadPose("TurnRight") ?? Load("LookRight");
        winkFace = wink != null ? wink : normal;
        lookLeft = left != null ? left : normal;
        lookRight = right != null ? right : normal;
        return true;
    }
    public static Sprite Blink
    {
        get { if (blink == null) blink = LoadPose("Blink"); return blink; }
    }
    private static Sprite LoadPose(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("PuppyAvatarV2/" + name);
        if (texture == null) return null;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), 100f);
        sprite.name = "PuppyAvatarV2_" + name; return sprite;
    }
    private static Sprite Load(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("PuppyAvatarV1/" + name);
        if (texture == null) return null;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
        sprite.name = "PuppyAvatarV1_" + name;
        return sprite;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOwnedSprites()
    {
        foreach (Sprite sprite in new[] { normal, wink, left, right, blink })
            if (sprite != null) Object.Destroy(sprite);
        normal = wink = left = right = blink = null; warned = false;
    }
}
