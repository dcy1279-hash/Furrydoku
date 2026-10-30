using UnityEngine;
using UnityEngine.UI;

// Shared completion light (reference look): the cell turns into a paler version
// of its OWN colour, with only a hair-thin soft edge (no haze into the gaps). No white rim, no gold seam lines. Pure mesh helper; no gameplay access.
public static class CellLightWash
{
    public const float Duration = .42f;
    private const int CornerSteps = 6;

    // Fast rise, short hold, slow fade. age in seconds since this cell lit.
    public static float Envelope(float age)
    {
        if (age <= 0f || age >= Duration) return 0f;
        if (age < .07f) return Smooth(age / .07f);
        if (age < .15f) return 1f;
        return 1f - Smooth((age - .15f) / (Duration - .15f));
    }

    // strength 0..1; regionColor = the cell's own fill. uv must hit an opaque white texel.
    public static void Draw(VertexHelper mesh, Rect cell, float cornerRatio, float strength, Color regionColor, Vector2 uv)
    {
        if (strength <= .002f) return;
        float unit = Mathf.Min(cell.width, cell.height);
        float radius = unit * cornerRatio;
        Color pale = Color.Lerp(regionColor, Color.white, .50f);
        Color hue = Color.Lerp(regionColor, Color.white, .12f);
        Color face = new Color(pale.r, pale.g, pale.b, .38f * strength);
        Color edge = new Color(Mathf.Lerp(pale.r, hue.r, .5f), Mathf.Lerp(pale.g, hue.g, .5f), Mathf.Lerp(pale.b, hue.b, .5f), .40f * strength);
        Color clear = new Color(hue.r, hue.g, hue.b, 0f);
        // Paler face of the same colour (inner rectangle + rounded band).
        Rect inner = Rect.MinMaxRect(cell.xMin + radius, cell.yMin + radius, cell.xMax - radius, cell.yMax - radius);
        if (inner.width > 0f && inner.height > 0f) Quad(mesh, inner, face, uv);
        float soft = unit * .06f;
        Ring(mesh, cell, radius, -radius, -soft, face, face, uv);
        // Soft transition right at the edge only. No haze out into the gaps: neighbouring hazes
        // overlapped there and read as a square shadow frame around every cell.
        Ring(mesh, cell, radius, -soft, 0f, face, edge, uv);
        Ring(mesh, cell, radius, 0f, unit * .025f, edge, clear, uv);
    }

    // Saturated version of a pastel region colour, for sparkles that read as colour, not white.
    public static Color Vivid(Color c)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float v);
        return Color.HSVToRGB(h, Mathf.Min(1f, sat * 1.9f + .18f), Mathf.Min(1f, v * 1.02f));
    }

    private static readonly Color Gold = new Color(1f, .78f, .22f);
    private static readonly Color Amber = new Color(1f, .62f, .16f);

    // Stars and glow dots flying outward from a lit cell. age: seconds since it lit.
    // Mostly gold/amber; only every fourth particle takes the cell's own vivid hue.
    public static void Sparkles(VertexHelper mesh, Vector2 centre, float unit, Color regionColor,
        float age, int count, int seed, Vector2 uv, float reach = 1f)
    {
        Color vivid = Vivid(regionColor);
        for (int i = 0; i < count; i++)
        {
            float h1 = Hash(seed * 31 + i * 7), h2 = Hash(seed * 17 + i * 13 + 5), h3 = Hash(seed * 7 + i * 3 + 11);
            float start = .02f + h1 * .10f, life = .38f + h2 * .22f;
            float p = (age - start) / life;
            if (p <= 0f || p >= 1f) continue;
            float angle = (i + h3 * .8f) / count * Mathf.PI * 2f + seed;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float travel = unit * (.30f + .55f * h2) * reach;
            float ease = 1f - (1f - p) * (1f - p);
            Vector2 pos = centre + dir * (unit * .35f + travel * ease) + Vector2.down * unit * .12f * p * p;
            float alpha = Mathf.Sin(Mathf.PI * Mathf.Min(1f, p * 1.3f)) * (1f - .3f * p);
            Color tint = i % 4 == 3 ? Color.Lerp(vivid, Gold, .35f) : (h3 > .45f ? Gold : Amber);
            float size = unit * (.072f + .072f * h1) * (1f - .35f * p);
            Glow(mesh, pos, size * 2.1f, new Color(tint.r, tint.g, tint.b, alpha * .35f), uv);
            if (i % 2 == 0) Star(mesh, pos, size * 1.5f, Color.Lerp(tint, Color.white, .22f), alpha, uv);
            else Glow(mesh, pos, size * .7f, new Color(tint.r, tint.g, tint.b, alpha), uv);
        }
    }

    private static void Glow(VertexHelper mesh, Vector2 p, float r, Color c, Vector2 uv)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(p, c, uv);
        Color clear = new Color(c.r, c.g, c.b, 0f);
        for (int j = 0; j < 12; j++)
        { float a = j * Mathf.PI / 6f; mesh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, clear, uv); }
        for (int j = 0; j < 12; j++) mesh.AddTriangle(first, first + 1 + j, first + 1 + (j + 1) % 12);
    }

    private static void Star(VertexHelper mesh, Vector2 p, float r, Color c, float alpha, Vector2 uv)
    {
        Color k = new Color(c.r, c.g, c.b, alpha);
        for (int arm = 0; arm < 2; arm++)
        {
            Vector2 a = arm == 0 ? Vector2.up : Vector2.right, b = arm == 0 ? Vector2.right : Vector2.down;
            int i = mesh.currentVertCount;
            mesh.AddVert(p + a * r, k, uv); mesh.AddVert(p + b * r * .24f, k, uv);
            mesh.AddVert(p - a * r, k, uv); mesh.AddVert(p - b * r * .24f, k, uv);
            mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        }
    }

    private static float Hash(int v) { float x = Mathf.Sin(v * 12.9898f + 78.233f) * 43758.5453f; return x - Mathf.Floor(x); }

    private static void Quad(VertexHelper mesh, Rect r, Color c, Vector2 uv)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(new Vector2(r.xMin, r.yMin), c, uv); mesh.AddVert(new Vector2(r.xMax, r.yMin), c, uv);
        mesh.AddVert(new Vector2(r.xMax, r.yMax), c, uv); mesh.AddVert(new Vector2(r.xMin, r.yMax), c, uv);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Ring(VertexHelper mesh, Rect rect, float radius, float inner, float outer,
        Color innerColor, Color outerColor, Vector2 uv)
    {
        const int points = 4 * (CornerSteps + 1);
        int start = mesh.currentVertCount;
        for (int p = 0; p < points; p++)
        {
            mesh.AddVert(Point(rect, radius, inner, p), innerColor, uv);
            mesh.AddVert(Point(rect, radius, outer, p), outerColor, uv);
        }
        for (int p = 0; p < points; p++)
        {
            int a = start + p * 2, b = start + ((p + 1) % points) * 2;
            mesh.AddTriangle(a, a + 1, b + 1); mesh.AddTriangle(a, b + 1, b);
        }
    }

    private static Vector2 Point(Rect rect, float radius, float offset, int point)
    {
        int corner = point / (CornerSteps + 1);
        float angle = (corner * 90f + point % (CornerSteps + 1) * 90f / CornerSteps) * Mathf.Deg2Rad;
        Vector2 center = new Vector2(
            corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
            corner < 2 ? rect.yMax - radius : rect.yMin + radius);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Max(0f, radius + offset);
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
