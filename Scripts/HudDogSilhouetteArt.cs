using UnityEngine;

// Flat white mask, tinted by the actual region colour. A HUD owns one shared
// sprite, not one texture per slot. No face/answer data in unknown slots.
public static class HudDogSilhouetteArt
{
    public static Sprite CreateSprite()
    {
        // Approved option C: the puppy avatar's own outline (same canvas, so the found
        // avatar lands exactly on it), white mask tinted by the region colour.
        Sprite approved = SharedUiSprites.Get("UiIcons/hud_puppy_silhouette", "HudAvatarOutlineSilhouette");
        if (approved != null) return approved;
        const int side = 192;
        var pixels = new Color32[side * side];
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        {
            // Fill more of the slot without clipping the floppy ear tips.
            Vector2 p = new Vector2((x + .5f) / side - .5f, (y + .5f) / side - .5f) * .90f;
            // Smooth forehead and clear drooping ears, no central hair tufts/bumps.
            float d = Ellipse(p - new Vector2(0f, -.035f), new Vector2(.285f, .315f));
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 q = p - new Vector2(sign * .285f, .015f);
                float a = sign * .12f;
                Vector2 ear = new Vector2(Mathf.Cos(a) * q.x + Mathf.Sin(a) * q.y,
                    -Mathf.Sin(a) * q.x + Mathf.Cos(a) * q.y);
                d = Mathf.Min(d, Ellipse(ear, new Vector2(.135f, .235f)));
            }
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f / side, 1f / side, d));
            pixels[y * side + x] = new Color(1f, 1f, 1f, alpha);
        }
        var texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
        { name = "HudUnknownPuppyMask", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, side, side), Vector2.one * .5f,
            100f, 0, SpriteMeshType.FullRect);
        sprite.name = "HudRegionColouredPuppySilhouette";
        return sprite;
    }
    private static float Ellipse(Vector2 p, Vector2 radius) =>
        (new Vector2(p.x / radius.x, p.y / radius.y).magnitude - 1f) * Mathf.Min(radius.x, radius.y);
}
