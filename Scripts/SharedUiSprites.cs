using System.Collections.Generic;
using UnityEngine;

// Sprites made straight from Resources textures, shared by every HUD and never destroyed.
// (Copying them with Object.Instantiate works in the editor but gives black textures on phones:
// imported textures are not CPU-readable in a player build.) Callers that build their own
// fallback textures still own and destroy those; use IsShared to tell them apart.
public static class SharedUiSprites
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => cache.Clear();

    public static Sprite Get(string path, string name)
    {
        if (cache.TryGetValue(path, out Sprite sprite) && sprite != null) return sprite;
        Texture2D art = Resources.Load<Texture2D>(path);
        if (art == null) return null;
        sprite = Sprite.Create(art, new Rect(0f, 0f, art.width, art.height), Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        cache[path] = sprite;
        return sprite;
    }

    public static bool IsShared(Sprite sprite) => sprite != null && cache.ContainsValue(sprite);

    // For HUD cleanup: destroys only sprites (and their textures) the caller generated itself.
    public static void ReleaseOwned(Sprite sprite)
    {
        if (sprite == null || IsShared(sprite)) return;
        if (sprite.texture != null) Object.Destroy(sprite.texture);
        Object.Destroy(sprite);
    }
}
