using System;
using UnityEngine;
using UnityEngine.UI;

// Victory-only bitmap glyphs exported from the approved preview. No dependency
// on a Chinese system font on the player's phone; no font file is redistributed.
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PuppyVictoryGlyphText : MaskableGraphic
{
    [Serializable] public sealed class Glyph { public string character; public float advance; }
    [Serializable] public sealed class Atlas
    {
        public int columns, rows, cellSize;
        public float baseFontSize;
        public Glyph[] glyphs;
    }
    private Texture2D atlasTexture;
    private Atlas atlas;
    private string value = "";
    private float fontSize = 24f;
    public override Texture mainTexture => atlasTexture != null ? atlasTexture : Texture2D.whiteTexture;

    public void Configure(Texture2D texture, TextAsset metadata)
    {
        atlasTexture = texture;
        atlas = JsonUtility.FromJson<Atlas>(metadata.text);
        if (atlas == null || atlas.columns < 1 || atlas.rows < 1 ||
            atlas.baseFontSize <= 0f || atlas.glyphs == null)
            throw new InvalidOperationException("PuppyVictoryGlyphText: invalid glyph atlas.");
        raycastTarget = false;
        SetAllDirty();
    }

    public void SetText(string text, float size = 23.4f)
    {
        value = text ?? "";
        fontSize = Mathf.Max(1f, size);
        SetVerticesDirty();
    }

    private int Find(char character)
    {
        for (int i = 0; i < atlas.glyphs.Length; i++)
            if (atlas.glyphs[i].character.Length == 1 && atlas.glyphs[i].character[0] == character)
                return i;
        return -1;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (atlasTexture == null || atlas == null || atlas.glyphs == null) return;
        float scale = fontSize / atlas.baseFontSize;
        float width = 0f;
        foreach (char character in value)
        {
            int i = Find(character);
            if (i >= 0) width += atlas.glyphs[i].advance * scale;
        }
        float x = rectTransform.rect.center.x - width * .5f;
        float cy = rectTransform.rect.center.y;
        float side = atlas.cellSize / 3f * scale;
        foreach (char character in value)
        {
            int i = Find(character);
            if (i < 0) continue;
            float advance = atlas.glyphs[i].advance * scale;
            float center = x + advance * .5f;
            float u0 = (i % atlas.columns) / (float)atlas.columns;
            float u1 = u0 + 1f / atlas.columns;
            float v1 = 1f - (i / atlas.columns) / (float)atlas.rows;
            float v0 = v1 - 1f / atlas.rows;
            int first = vh.currentVertCount;
            vh.AddVert(new Vector3(center - side * .5f, cy - side * .5f), color, new Vector2(u0, v0));
            vh.AddVert(new Vector3(center - side * .5f, cy + side * .5f), color, new Vector2(u0, v1));
            vh.AddVert(new Vector3(center + side * .5f, cy + side * .5f), color, new Vector2(u1, v1));
            vh.AddVert(new Vector3(center + side * .5f, cy - side * .5f), color, new Vector2(u1, v0));
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first, first + 2, first + 3);
            x += advance;
        }
    }
}
