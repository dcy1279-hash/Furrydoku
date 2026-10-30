using UnityEngine;
using UnityEngine.UI;

// One non-interactive mesh for gold seam glow and brief gold dust.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CorrectPlacementGraphic : MaskableGraphic
{
    private CorrectPlacementFeedback.Burst[] bursts;
    public bool IsEmpty { get; private set; } = true;
    public void SetBursts(CorrectPlacementFeedback.Burst[] value) { bursts = value; SetVerticesDirty(); }
    public void RefreshFrame(bool empty) { IsEmpty = empty; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (bursts == null) return;
        foreach (var burst in bursts) if (burst.Active)
        {
            // A completed colour lights up cell by cell (reference look); its gold
            // seams stay as a faint accent underneath the light.
            if (burst.CompletedRegion && burst.Rects != null)
                for (int j = 0; j < burst.Rects.Length; j++)
                    CellLightWash.Draw(vh, Grow(burst.Rects[j], CorrectPlacementFeedback.Pulse(
                        burst.Time - burst.Delays[j] - burst.ScaleLag) * burst.ScaleAmount),
                        BoardArtStyle.CellCornerRatio, CellLightWash.Envelope(burst.Time - burst.Delays[j]),
                        burst.Colors != null && j < burst.Colors.Length ? burst.Colors[j] : Color.white, Vector2.zero);
            if (burst.CompletedRegion && burst.Rects != null)
                for (int j = 0; j < burst.Rects.Length; j++)
                    CellLightWash.Sparkles(vh, burst.Rects[j].center, burst.Side,
                        burst.Colors != null && j < burst.Colors.Length ? burst.Colors[j] : Color.white,
                        burst.Time - burst.Delays[j], 2, burst.Seed + j * 3, Vector2.zero, 1.1f);
            // Reference has no bright seam lines on a completed colour; single placements keep theirs.
            float seamAlpha = burst.CompletedRegion ? 0f : 1f;
            foreach (var edge in burst.Edges)
            {
                float pulse = CorrectPlacementFeedback.Pulse(burst.Time - edge.Delay) * seamAlpha;
                if (pulse <= .001f) continue;
                GlowLine(vh, edge.A, edge.B, burst.Side, pulse);
            }
            // Glints on/near seams; a completed colour gets a richer shower.
            int glints = burst.CompletedRegion ? 0 : 18;
            for (int i = 0; i < glints; i++)
            {
                float r = Hash(burst.Seed + i * 7), r2 = Hash(burst.Seed + i * 13 + 3);
                float start = .025f + r * .16f, lifetime = .24f + r2 * .14f;
                var seam = burst.Edges[(i * 7 + burst.Seed) % burst.Edges.Length];
                if (burst.CompletedRegion) start = seam.Delay + .025f;
                float p = (burst.Time - start) / lifetime;
                if (p <= 0f || p >= 1f) continue;
                Vector2 source;
                if (i < 8 && !burst.CompletedRegion) source = burst.Origin;
                else
                {
                    source = Vector2.Lerp(seam.A, seam.B, r2);
                }
                float angle = (i * 2.39996f + r) % (Mathf.PI * 2f);
                Vector2 outward = source - burst.Origin;
                if (outward.sqrMagnitude < 1f) outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                outward.Normalize();
                float reach = 1f;
                Vector2 position = source + outward * burst.Side * (.18f + .27f * r) * reach * p +
                    Vector2.down * burst.Side * .10f * p * p;
                float alpha = Mathf.Sin(p * Mathf.PI) * (1f - p * .4f);
                float radius = burst.Side * (.016f + .020f * r2) * (1f - .4f * p);
                Spot(vh, position, radius * 3.2f, new Color(1f, .70f, .08f, alpha * .26f));
                if (i % 3 == 0) Star(vh, position, radius * 2.4f, alpha);
                else Spot(vh, position, radius, new Color(1f, .94f, .52f, alpha));
            }
            float flash = 1f - Mathf.Clamp01(burst.Time / .16f);
            if (flash > 0f && !burst.CompletedRegion) Spot(vh, burst.Origin, burst.Side * .45f,
                new Color(1f, .80f, .26f, flash * .20f));
        }
    }
    private static Rect Grow(Rect r, float amount)
    { Vector2 size = r.size * (1f + amount); return new Rect(r.center - size * .5f, size); }
    private static float Hash(int value)
    { float v = Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f; return v - Mathf.Floor(v); }
    private static void GlowLine(VertexHelper vh, Vector2 a, Vector2 b, float side, float alpha)
    {
        SoftBand(vh, a, b, side * .12f, new Color(1f, .62f, .04f, alpha * .30f));
        SoftBand(vh, a, b, side * .055f, new Color(1f, .80f, .10f, alpha * .66f));
        SoftBand(vh, a, b, Mathf.Max(.8f, side * .016f), new Color(1f, .98f, .68f, alpha));
    }
    private static void SoftBand(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        Color clear = new Color(c.r, c.g, c.b, 0f);
        Quad(vh, a - n, a, b, b - n, clear, c, c, clear);
        Quad(vh, a, a + n, b + n, b, c, clear, clear, c);
    }
    private static void Spot(VertexHelper vh, Vector2 point, float radius, Color c)
    {
        int first = vh.currentVertCount;
        vh.AddVert(point, c, Vector2.zero);
        Color clear = new Color(c.r, c.g, c.b, 0f);
        for (int j = 0; j < 16; j++)
        { float a = j * Mathf.PI / 8f; vh.AddVert(point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, clear, Vector2.zero); }
        for (int j = 0; j < 16; j++) vh.AddTriangle(first, first + 1 + j, first + 1 + (j + 1) % 16);
    }
    private static void Star(VertexHelper vh, Vector2 p, float radius, float alpha)
    {
        Color c = new Color(1f, 1f, .80f, alpha);
        Quad(vh, p + Vector2.up * radius, p + Vector2.right * radius * .25f,
            p + Vector2.down * radius, p + Vector2.left * radius * .25f, c, c, c, c);
        Quad(vh, p + Vector2.right * radius, p + Vector2.down * radius * .25f,
            p + Vector2.left * radius, p + Vector2.up * radius * .25f, c, c, c, c);
    }
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color ca, Color cb, Color cc, Color cd)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, ca, Vector2.zero); vh.AddVert(b, cb, Vector2.zero);
        vh.AddVert(c, cc, Vector2.zero); vh.AddVert(d, cd, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }
}
