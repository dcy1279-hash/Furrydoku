using UnityEngine;

// Generated ivory cartoon artwork, with an owned HUD texture copy.
// One owned sprite per HUD is reused for all life icons and loss overlays.
public static class LifeBoneArt
{
    // Approved cream bone with paw (hud_collection_v1, option C); grey version for lost lives.
    // Shared (never copied) Resources art; see SharedUiSprites for why copies turn black on phones.
    public static Sprite CreateLostSprite() => SharedUiSprites.Get("UiIcons/hud_bone_off", "HudLostLifeBone");
    public static Sprite CreateSprite()
    {
        Sprite approved = SharedUiSprites.Get("UiIcons/hud_bone_on", "HudCreamLifeBone");
        if (approved != null) return approved;
        Sprite imported = SharedUiSprites.Get("LifeBonesV2/WhiteCartoonBonePaw", "HudIvoryCartoonLifeBone");
        if (imported != null) return imported;
        Debug.LogWarning("LifeBoneArt：奶白骨头素材未导入，暂用备用图标；请等待导入完成后重新进入。");
        const int width = 448, height = 256;
        var pixels = new Color32[width * height];
        Vector3 light = new Vector3(-.43f, .68f, .72f).normalized;
        Vector3 halfLight = (light + Vector3.forward).normalized;
        float aa = 1.25f / height, epsilon = .0025f;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            Vector2 p = new Vector2((x + .5f - width * .5f) / height,
                (y + .5f - height * .5f) / height);
            float d = Distance(p), coverage = 1f - SmoothRange(-aa, aa, d);
            Vector2 gradient = new Vector2(Distance(p + Vector2.right * epsilon) - Distance(p - Vector2.right * epsilon),
                Distance(p + Vector2.up * epsilon) - Distance(p - Vector2.up * epsilon)).normalized;
            float radial = Mathf.Clamp01(1f - Mathf.Max(0f, -d) / .19f);
            Vector3 normal = new Vector3(gradient.x * radial, gradient.y * radial,
                Mathf.Sqrt(Mathf.Max(0f, 1f - radial * radial)));
            float diffuse = Mathf.Clamp01(Vector3.Dot(normal, light));
            // Warm matte-toy gold, not polished metal: broad, restrained highlights.
            Color fill = Color.Lerp(new Color(.70f, .43f, .14f), new Color(.94f, .70f, .30f),
                Mathf.Clamp01(.25f + diffuse * .75f));
            float specular = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, halfLight)), 12f);
            fill = Color.Lerp(fill, new Color(1f, .87f, .59f), specular * .22f);
            float softTop = Mathf.Exp(-Mathf.Pow((p.y - .19f) / .12f, 2f)) * SmoothRange(.01f, .07f, -d);
            fill = Color.Lerp(fill, new Color(.97f, .80f, .46f), softTop * .10f);
            float shadow = (1f - SmoothRange(-aa, aa, Distance(p + new Vector2(-.008f, .023f)))) * .16f;
            float alpha = coverage + shadow * (1f - coverage);
            Color shade = new Color(.55f, .29f, .07f);
            Color output = alpha > 0f ? (fill * coverage + shade * shadow * (1f - coverage)) / alpha : Color.clear;
            output.a = alpha; pixels[y * width + x] = output;
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { name = "MatteCartoonVolumeBone", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f,
            100f, 0, SpriteMeshType.FullRect);
        sprite.name = "HudRounded3DStyleBone";
        return sprite;
    }
    private static float SmoothRange(float from, float to, float value) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    private static float Distance(Vector2 p)
    {
        Vector2 axis = new Vector2(Mathf.Clamp(p.x, -.56f, .56f), 0f);
        float d = (p - axis).magnitude - .20f;
        for (int end = -1; end <= 1; end += 2) for (int lobe = -1; lobe <= 1; lobe += 2)
        {
            float circle = (p - new Vector2(end * .57f, lobe * .150f)).magnitude - .250f;
            float h = Mathf.Clamp01(.5f + .5f * (circle - d) / .075f);
            d = Mathf.Lerp(circle, d, h) - .075f * h * (1f - h);
        }
        return d;
    }
}
