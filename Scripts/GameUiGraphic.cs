using UnityEngine;
using UnityEngine.UI;

// Small native UI shapes: no screenshots, downloaded icons or answer data.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GameUiGraphic : MaskableGraphic
{
    public enum Kind { RoundedPanel, Back, Gear, Paw, RuleColor, RuleLine, RuleNeighbours, Exclamation, TooltipTail, Ring,
        Speaker, Music, Voice, Vibrate, Palette, Close, Slash }
    // Settings icons draw in `color`; Palette keeps its own tile colours unless Muted.
    public bool Muted;
    public Kind Shape;
    public float CornerRatio = .18f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        if (Shape == Kind.RoundedPanel) { Rounded(vh, r, color); return; }
        if (Shape == Kind.Ring) { RingShape(vh, r, color); return; }
        if (Shape == Kind.TooltipTail)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector2(r.center.x, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            return;
        }
        float side = Mathf.Min(r.width, r.height);
        Vector2 c = r.center;
        // Style A (soft flat): rounded strokes, round-toothed gear with a paw, round '!'.
        if (Shape == Kind.Back)
        {
            float w = side * .12f;
            Vector2 tip = c + new Vector2(-.13f, 0f) * side;
            RoundLine(vh, c + new Vector2(.13f, .27f) * side, tip, w, color);
            RoundLine(vh, tip, c + new Vector2(.13f, -.27f) * side, w, color);
        }
        else if (Shape == Kind.Gear)
        {
            const int teeth = 8, segments = 128;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
                Quad(vh, c + Direction(a) * side * .22f, c + Direction(a) * side * GearRadius(a, teeth),
                    c + Direction(b) * side * GearRadius(b, teeth), c + Direction(b) * side * .22f, color);
            }
            Paw(vh, c + Vector2.down * side * .005f, side * .34f, color);
        }
        else if (Shape == Kind.Paw) Paw(vh, c, side, color);
        else if (Shape == Kind.Speaker)
        {
            Vector2 o = c + Vector2.left * side * .08f;
            int i = vh.currentVertCount;   // cone + box as one polygon
            foreach (Vector2 v in new[] { new Vector2(-.24f, -.09f), new Vector2(-.10f, -.09f), new Vector2(.04f, -.22f),
                new Vector2(.04f, .22f), new Vector2(-.10f, .09f), new Vector2(-.24f, .09f) })
                vh.AddVert(o + v * side, color, Vector2.zero);
            for (int k = 1; k < 5; k++) vh.AddTriangle(i, i + k, i + k + 1);
            Arc(vh, o + Vector2.right * side * .06f, side * .14f, -50f, 50f, side * .055f, color);
            Arc(vh, o + Vector2.right * side * .06f, side * .26f, -48f, 48f, side * .055f, color);
        }
        else if (Shape == Kind.Music)
        {
            float w = side * .07f;
            RoundLine(vh, c + new Vector2(-.12f, -.20f) * side, c + new Vector2(-.12f, .20f) * side, w, color);
            RoundLine(vh, c + new Vector2(.18f, -.15f) * side, c + new Vector2(.18f, .25f) * side, w, color);
            RoundLine(vh, c + new Vector2(-.12f, .20f) * side, c + new Vector2(.18f, .25f) * side, w * 1.4f, color);
            Ellipse(vh, c + new Vector2(-.19f, -.22f) * side, new Vector2(.10f, .08f) * side, color);
            Ellipse(vh, c + new Vector2(.11f, -.17f) * side, new Vector2(.10f, .08f) * side, color);
        }
        else if (Shape == Kind.Voice)
        {
            RoundRect(vh, new Rect(c.x - side * .30f, c.y - side * .16f, side * .60f, side * .40f), side * .10f, color);
            int i = vh.currentVertCount;
            vh.AddVert(c + new Vector2(-.14f, -.15f) * side, color, Vector2.zero);
            vh.AddVert(c + new Vector2(-.02f, -.15f) * side, color, Vector2.zero);
            vh.AddVert(c + new Vector2(-.17f, -.30f) * side, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            for (int k = -1; k <= 1; k++) Ellipse(vh, c + new Vector2(k * .13f, .04f) * side, Vector2.one * side * .045f, Color.white);
        }
        else if (Shape == Kind.Vibrate)
        {
            RoundRect(vh, new Rect(c.x - side * .12f, c.y - side * .26f, side * .24f, side * .52f), side * .06f, color);
            RoundRect(vh, new Rect(c.x - side * .07f, c.y - side * .17f, side * .14f, side * .33f), side * .02f, new Color(1f, 1f, 1f, .85f * color.a));
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * .27f, w = side * .045f;
                RoundLine(vh, c + new Vector2(x, .14f) * side, c + new Vector2(x + s * .04f, .07f) * side, w, color);
                RoundLine(vh, c + new Vector2(x + s * .04f, .07f) * side, c + new Vector2(x, 0f) * side, w, color);
                RoundLine(vh, c + new Vector2(x, 0f) * side, c + new Vector2(x + s * .04f, -.07f) * side, w, color);
                RoundLine(vh, c + new Vector2(x + s * .04f, -.07f) * side, c + new Vector2(x, -.14f) * side, w, color);
            }
        }
        else if (Shape == Kind.Palette)
        {
            Color grey = color;
            Color[] tiles = { new Color(.97f, .65f, .63f), new Color(.62f, .83f, .66f), new Color(.55f, .76f, .95f), new Color(.97f, .65f, .63f) };
            float t = side * .25f, g = side * .03f;
            for (int k = 0; k < 4; k++)
            {
                Vector2 o = c + new Vector2(k % 2 == 0 ? -t - g * .5f : g * .5f, k < 2 ? g * .5f : -t - g * .5f);
                RoundRect(vh, new Rect(o.x, o.y, t, t), t * .22f, Muted ? new Color(.83f, .78f, .73f, color.a) : tiles[k]);
            }
            Vector2 q = Vector2.one * (t * .5f + g * .5f);
            Ellipse(vh, c + new Vector2(-q.x, q.y), Vector2.one * t * .2f, grey);
            int j = vh.currentVertCount; Vector2 tc = c + q;
            vh.AddVert(tc + new Vector2(0f, .24f) * t, grey, Vector2.zero); vh.AddVert(tc + new Vector2(.24f, -.2f) * t, grey, Vector2.zero);
            vh.AddVert(tc + new Vector2(-.24f, -.2f) * t, grey, Vector2.zero); vh.AddTriangle(j, j + 1, j + 2);
            Ellipse(vh, c + new Vector2(-q.x, -q.y), Vector2.one * t * .2f, grey);
            Ellipse(vh, c + new Vector2(q.x, -q.y), Vector2.one * t * .2f, grey);
        }
        else if (Shape == Kind.Close)
        {
            RoundLine(vh, c + new Vector2(-.18f, -.18f) * side, c + new Vector2(.18f, .18f) * side, side * .12f, color);
            RoundLine(vh, c + new Vector2(-.18f, .18f) * side, c + new Vector2(.18f, -.18f) * side, side * .12f, color);
        }
        else if (Shape == Kind.Slash)
            RoundLine(vh, c + new Vector2(-.30f, -.30f) * side, c + new Vector2(.30f, .30f) * side, side * .07f, color);
        else if (Shape == Kind.Exclamation)
        {
            RoundLine(vh, c + Vector2.up * side * .27f, c - Vector2.up * side * .05f, side * .16f, color);
            Ellipse(vh, c - Vector2.up * side * .27f, Vector2.one * side * .085f, color);
        }
        else Rules(vh, r);
    }

    private static Vector2 Direction(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    private void Rounded(VertexHelper vh, Rect r, Color shade)
    {
        float radius = Mathf.Min(r.width, r.height) * Mathf.Clamp(CornerRatio, 0f, .5f);
        int centre = vh.currentVertCount;
        vh.AddVert(r.center, shade, Vector2.zero);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 origin = corner == 0 ? new Vector2(r.xMax - radius, r.yMax - radius) :
                corner == 1 ? new Vector2(r.xMin + radius, r.yMax - radius) :
                corner == 2 ? new Vector2(r.xMin + radius, r.yMin + radius) :
                new Vector2(r.xMax - radius, r.yMin + radius);
            for (int step = 0; step <= 8; step++)
                vh.AddVert(origin + Direction((corner * 90f + step * 90f / 8f) * Mathf.Deg2Rad) * radius,
                    shade, Vector2.zero);
        }
        for (int i = 0; i < 36; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 1 + (i + 1) % 36);
        // ~1 screen pixel soft edge so curves don't look jagged/blurry at small sizes.
        float f = PixelSize() * 1.2f;
        if (f <= 0f) return;
        int ring = vh.currentVertCount;
        Color clear = new Color(shade.r, shade.g, shade.b, 0f);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 origin = corner == 0 ? new Vector2(r.xMax - radius, r.yMax - radius) :
                corner == 1 ? new Vector2(r.xMin + radius, r.yMax - radius) :
                corner == 2 ? new Vector2(r.xMin + radius, r.yMin + radius) :
                new Vector2(r.xMax - radius, r.yMin + radius);
            for (int step = 0; step <= 8; step++)
                vh.AddVert(origin + Direction((corner * 90f + step * 90f / 8f) * Mathf.Deg2Rad) * (radius + f), clear, Vector2.zero);
        }
        for (int i = 0; i < 36; i++)
        {
            int a = centre + 1 + i, b = centre + 1 + (i + 1) % 36, oa = ring + i, ob = ring + (i + 1) % 36;
            vh.AddTriangle(a, oa, ob); vh.AddTriangle(a, ob, b);
        }
    }
    // Size of one screen pixel in this graphic's local units.
    private float PixelSize()
    {
        Canvas c = canvas;
        if (c == null) return 0f;
        float scale = c.scaleFactor * Mathf.Abs(rectTransform.lossyScale.x / Mathf.Max(1e-5f, c.transform.lossyScale.x));
        return scale > 1e-5f ? 1f / scale : 0f;
    }

    private void Rules(VertexHelper vh, Rect r)
    {
        float size = Mathf.Min(r.width, r.height), cell = size / 3f;
        Vector2 origin = r.center - Vector2.one * size * .5f;
        Color tile = new Color(.82f, .71f, .61f), ink = new Color(.53f, .37f, .31f);
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
        {
            Vector2 c = origin + new Vector2(x + .5f, y + .5f) * cell;
            float half = cell * .45f;
            int row = 2 - y;
            // Three different proofs: an L-shaped COLOUR region; a top-row /
            // middle-column T through its animal; all eight NEIGHBOURS.
            bool selected = Shape == Kind.RuleColor ? row == 0 || x == 0 :
                Shape == Kind.RuleLine ? row == 0 || x == 1 : true;
            bool animal = Shape == Kind.RuleColor ? x == 0 && row == 1 :
                Shape == Kind.RuleLine ? x == 1 && row == 0 : x == 1 && row == 1;
            bool excluded = selected && !animal;
            // All three pictures share one tile colour so the rules read as one set.
            Color background = !selected ? new Color(.95f, .90f, .84f) : tile;
            Rounded(vh, new Rect(c - Vector2.one * half, Vector2.one * half * 2f), background);
            if (animal) Paw(vh, c, cell * .78f, ink);
            else if (excluded)
            {
                float d = cell * .23f;
                Line(vh, c - Vector2.one * d, c + Vector2.one * d, cell * .09f, Color.white);
                Line(vh, c + new Vector2(-d, d), c + new Vector2(d, -d), cell * .09f, Color.white);
            }
        }
    }

    private static void Paw(VertexHelper vh, Vector2 c, float size, Color shade)
    {
        Ellipse(vh, c + Vector2.down * size * .12f, new Vector2(.24f, .22f) * size, shade);
        for (int i = 0; i < 4; i++)
            Ellipse(vh, c + new Vector2((i - 1.5f) * .19f, i == 0 || i == 3 ? .15f : .29f) * size,
                new Vector2(.085f, .11f) * size, shade);
    }
    private static void Ellipse(VertexHelper vh, Vector2 c, Vector2 radius, Color shade)
    {
        int start = vh.currentVertCount;
        vh.AddVert(c, shade, Vector2.zero);
        for (int i = 0; i < 24; i++) vh.AddVert(c + Vector2.Scale(Direction(i * Mathf.PI / 12f), radius), shade, Vector2.zero);
        for (int i = 0; i < 24; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 24);
    }
    // Rounded gear profile: flat-topped teeth with smooth shoulders.
    private static float GearRadius(float angle, int teeth)
    {
        float t = Mathf.Repeat(angle * teeth / (Mathf.PI * 2f), 1f);
        float up = Mathf.SmoothStep(0f, 1f, t / .10f) * (1f - Mathf.SmoothStep(0f, 1f, (t - .44f) / .10f));
        return .355f + (.46f - .355f) * up;
    }
    private static void RoundRect(VertexHelper vh, Rect r, float radius, Color shade)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) * .5f);
        int centre = vh.currentVertCount;
        vh.AddVert(r.center, shade, Vector2.zero);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 origin = corner == 0 ? new Vector2(r.xMax - radius, r.yMax - radius) :
                corner == 1 ? new Vector2(r.xMin + radius, r.yMax - radius) :
                corner == 2 ? new Vector2(r.xMin + radius, r.yMin + radius) : new Vector2(r.xMax - radius, r.yMin + radius);
            for (int step = 0; step <= 6; step++)
                vh.AddVert(origin + Direction((corner * 90f + step * 15f) * Mathf.Deg2Rad) * radius, shade, Vector2.zero);
        }
        for (int i = 0; i < 28; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 1 + (i + 1) % 28);
    }
    private static void Arc(VertexHelper vh, Vector2 c, float radius, float fromDeg, float toDeg, float width, Color shade)
    {
        const int steps = 10;
        for (int i = 0; i < steps; i++)
        {
            float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad, b = Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)steps) * Mathf.Deg2Rad;
            Line(vh, c + Direction(a) * radius, c + Direction(b) * radius, width, shade);
        }
        Ellipse(vh, c + Direction(fromDeg * Mathf.Deg2Rad) * radius, Vector2.one * width * .5f, shade);
        Ellipse(vh, c + Direction(toDeg * Mathf.Deg2Rad) * radius, Vector2.one * width * .5f, shade);
    }
    private static void RoundLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color shade)
    {
        Line(vh, a, b, width, shade);
        Ellipse(vh, a, Vector2.one * width * .5f, shade);
        Ellipse(vh, b, Vector2.one * width * .5f, shade);
    }
    // Thin outline just inside a circular button.
    private void RingShape(VertexHelper vh, Rect r, Color shade)
    {
        float outer = Mathf.Min(r.width, r.height) * .5f, inner = outer * .955f, f = PixelSize() * 1.1f;
        Color clear = new Color(shade.r, shade.g, shade.b, 0f);
        const int segments = 96;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
            Vector2 da = Direction(a), db = Direction(b), c = r.center;
            Quad(vh, c + da * inner, c + da * outer, c + db * outer, c + db * inner, shade);
            if (f > 0f)
            {   // feather both edges
                Band(vh, c + da * outer, c + da * (outer + f), c + db * (outer + f), c + db * outer, shade, clear);
                Band(vh, c + da * inner, c + da * Mathf.Max(0f, inner - f), c + db * Mathf.Max(0f, inner - f), c + db * inner, shade, clear);
            }
        }
    }
    private static void Band(VertexHelper vh, Vector2 a, Vector2 aOut, Vector2 bOut, Vector2 b, Color solid, Color clear)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, solid, Vector2.zero); vh.AddVert(aOut, clear, Vector2.zero);
        vh.AddVert(bOut, clear, Vector2.zero); vh.AddVert(b, solid, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color shade)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        Quad(vh, a - n, a + n, b + n, b - n, shade);
    }
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color shade)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, shade, Vector2.zero); vh.AddVert(b, shade, Vector2.zero);
        vh.AddVert(c, shade, Vector2.zero); vh.AddVert(d, shade, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
