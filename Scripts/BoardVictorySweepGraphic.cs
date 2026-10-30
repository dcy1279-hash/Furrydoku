using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Textured internal seams only. Alpha gold/white cores and a separate screen
// halo keep colour on white gaps without drawing an LED-like perimeter.
// v4 (reference look): every cell turns a paler version of its own colour with
// a same-hue glow as the diagonal front reaches it. No gold seam lines or seam
// flares; only a soft board bloom and a few sparkles remain.
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BoardVictorySweepGraphic : MaskableGraphic
{
    private struct Seam { public Vector2 A, B; public float Side; }
    private struct Node { public Vector2 Point; public float Side, Seed; }
    private readonly List<Seam> seams = new List<Seam>(180);
    private readonly List<Node> nodes = new List<Node>(81);
    private readonly List<Rect> cellRects = new List<Rect>(100);
    private readonly List<Color> cellColors = new List<Color>(100);
    private CellLightLayer cellLight;
    private Texture2D atlas;
    private BoardVictoryGlowLayer halo;
    private Rect bounds;
    private float frameTime = -1f;
    private bool warned;
    public bool Ready => atlas != null && seams.Count > 0;
    public override Texture mainTexture => atlas != null ? atlas : Texture2D.whiteTexture;
    private static readonly Rect GoldUV = new Rect(0f, .75f, .5f, .25f);
    private static readonly Rect HaloUV = new Rect(0f, .5f, .5f, .25f);
    private static readonly Rect FlareUV = new Rect(.5f, 0f, .5f, 1f);
    // Opaque warm-white texel at the flare centre: a 'solid' UV for vertex-coloured fills.
    private static readonly Vector2 SolidUV = new Vector2(.75f, .5f);
    private const float SweepEnd = .51f, FinaleStart = .50f, FinaleEnd = 1.0f;
    private static readonly Color[] SparkTints =
    {
        new Color(1f, 1f, .94f), new Color(1f, .88f, .55f), new Color(1f, .97f, .82f), new Color(1f, .92f, .70f)
    };

    public void Configure(Rect[] cells, Color[] colors)
    {
        seams.Clear(); nodes.Clear(); cellRects.Clear(); cellColors.Clear(); frameTime = -1f;
        if (atlas == null) atlas = Resources.Load<Texture2D>("VictorySeamsV1/LightAtlas");
        if (atlas == null && !warned)
        { warned = true; Debug.LogWarning("胜利格缝：未找到LightAtlas，跳过庆祝并继续结算。", this); }
        int size = cells == null ? 0 : Mathf.RoundToInt(Mathf.Sqrt(cells.Length));
        if (size < 4 || size > 10 || size * size != cells.Length) { SetTime(-1f); return; }
        bounds = cells[0];
        cellRects.AddRange(cells);
        for (int i = 0; i < cells.Length; i++) cellColors.Add(colors != null && i < colors.Length ? colors[i] : Color.white);
        foreach (Rect cell in cells) bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, cell.xMin),
            Mathf.Min(bounds.yMin, cell.yMin), Mathf.Max(bounds.xMax, cell.xMax), Mathf.Max(bounds.yMax, cell.yMax));
        for (int i = 0; i < cells.Length; i++)
        {
            int row = i / size, col = i % size; Rect cell = cells[i];
            float side = Mathf.Min(cell.width, cell.height);
            float left = col == 0 ? cell.xMin : (cells[i - 1].xMax + cell.xMin) * .5f;
            float right = col == size - 1 ? cell.xMax : (cell.xMax + cells[i + 1].xMin) * .5f;
            float top = row == 0 ? cell.yMax : (cells[i - size].yMin + cell.yMax) * .5f;
            float bottom = row == size - 1 ? cell.yMin : (cell.yMin + cells[i + size].yMax) * .5f;
            if (col > 0) seams.Add(new Seam { A = new Vector2(left, bottom), B = new Vector2(left, top), Side = side });
            if (row > 0) seams.Add(new Seam { A = new Vector2(left, top), B = new Vector2(right, top), Side = side });
            if (col > 0 && row > 0)
                nodes.Add(new Node { Point = new Vector2(left, top), Side = side, Seed = col * 7f + row * 13f });
        }
        if (halo == null)
        {
            var go = new GameObject("ScreenSoftGlow", typeof(RectTransform)); go.layer = gameObject.layer;
            RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            halo = go.AddComponent<BoardVictoryGlowLayer>(); halo.Bind(this);
        }
        if (cellLight == null)
        {
            var go = new GameObject("CellLight", typeof(RectTransform)); go.layer = gameObject.layer;
            RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            cellLight = go.AddComponent<CellLightLayer>(); cellLight.raycastTarget = false;
            cellLight.Populate = DrawCellLight;
            rect.SetAsFirstSibling(); // under the soft bloom
        }
        SetTime(-1f);
    }
    public void SetTime(float time)
    {
        frameTime = time; SetVerticesDirty();
        if (halo != null) halo.SetVerticesDirty();
        if (cellLight != null) cellLight.SetVerticesDirty();
    }
    private float Diagonal(Vector2 p) =>
        (Mathf.InverseLerp(bounds.xMin, bounds.xMax, p.x) + Mathf.InverseLerp(bounds.yMin, bounds.yMax, p.y)) * .5f;
    public static float SampleAtTime(float diagonal, float time)
    {
        float age = time - BoardVictoryTiming.Arrival(diagonal);
        float head = Mathf.Exp(-.5f * age * age / (.033f * .033f));
        float tail = .18f * Smooth(age / .014f) * Mathf.Exp(-Mathf.Max(0f, age) / .06f);
        return Mathf.Clamp01(head + tail) * (1f - Smooth((time - .60f) / .12f));
    }
    private float Light(Vector2 p)
    {
        // Fade strip ends before they touch the outer white frame.
        float x = Mathf.Min(p.x - bounds.xMin, bounds.xMax - p.x) / bounds.width;
        float y = Mathf.Min(p.y - bounds.yMin, bounds.yMax - p.y) / bounds.height;
        return SampleAtTime(Diagonal(p), frameTime) * Smooth(Mathf.Min(x, y) / .034f);
    }
    protected override void OnPopulateMesh(VertexHelper mesh) => Populate(mesh, false);
    internal void Populate(VertexHelper mesh, bool screenLayer)
    {
        mesh.Clear();
        if (!Ready || frameTime < -.15f || frameTime > FinaleEnd) return;
        if (screenLayer) { DrawBloom(mesh); return; }
        if (!DrawSeams) return;
        foreach (Seam seam in seams)
        {
            bool vertical = Mathf.Abs(seam.B.y - seam.A.y) > Mathf.Abs(seam.B.x - seam.A.x);
            float width = seam.Side * (screenLayer ? .42f : .075f);
            const int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                Vector2 a = Vector2.Lerp(seam.A, seam.B, i / (float)steps);
                Vector2 b = Vector2.Lerp(seam.A, seam.B, (i + 1) / (float)steps);
                float la = Light(a), lb = Light(b);
                if (Mathf.Max(la, lb) < .002f) continue;
                Rect rect = vertical ? Rect.MinMaxRect(a.x - width * .5f, a.y, b.x + width * .5f, b.y) :
                    Rect.MinMaxRect(a.x, a.y - width * .5f, b.x, b.y + width * .5f);
                SpriteQuad(mesh, rect, screenLayer ? HaloUV : GoldUV, la, lb, vertical);
            }
        }
        DrawFlashes(mesh);
    }
    // The reference has no bright seam light; keep the old seam code switchable.
    private static readonly bool DrawSeams = false;
    // Each cell lights up (shared CellLightWash) when the diagonal front reaches it.
    private void DrawCellLight(VertexHelper mesh)
    {
        if (!Ready || frameTime < -.15f || frameTime > FinaleEnd) return;
        for (int i = 0; i < cellRects.Count; i++)
        {
            Rect cell = cellRects[i];
            float age = frameTime - BoardVictoryTiming.Arrival(Diagonal(cell.center));
            float strength = CellLightWash.Envelope(age);
            if (strength <= 0f) continue;
            float pop = BoardVictoryTiming.CellScale(Diagonal(cell.center), frameTime) - 1f;
            Vector2 size = cell.size * (1f + pop);
            CellLightWash.Draw(mesh, new Rect(cell.center - size * .5f, size), BoardArtStyle.CellCornerRatio,
                strength, cellColors[i], Vector2.zero);
        }
        DrawSeamHaze(mesh);
        // Sparkles fly out of each cell as it lights, plus a bigger burst off the rim at the end.
        for (int i = 0; i < cellRects.Count; i++)
        {
            Rect cell = cellRects[i];
            float age = frameTime - BoardVictoryTiming.Arrival(Diagonal(cell.center));
            if (age > 0f && age < .7f)
                CellLightWash.Sparkles(mesh, cell.center, Mathf.Min(cell.width, cell.height), cellColors[i], age, 1, i * 7 + 2, Vector2.zero);
        }
        DrawRimBurst(mesh);
    }
    // Wide, diffuse gold haze along the gaps (no thin bright core): ~0.34 cell across,
    // fading from the seam centre into the neighbouring cell edges as the front passes.
    private static readonly Color SeamGold = new Color(1f, .80f, .36f);
    private static readonly float[] HazeOffsets = { -1f, -.45f, 0f, .45f, 1f };
    private static readonly float[] HazeAlphas = { 0f, .55f, 1f, .55f, 0f };
    private void DrawSeamHaze(VertexHelper mesh)
    {
        const int steps = 10;
        foreach (Seam seam in seams)
        {
            Vector2 along = seam.B - seam.A;
            Vector2 across = new Vector2(-along.y, along.x).normalized * seam.Side * .17f;
            int first = mesh.currentVertCount;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = seam.A + along * (i / (float)steps);
                float age = frameTime - BoardVictoryTiming.Arrival(Diagonal(p));
                float glow = age <= 0f ? 0f : Smooth(age / .05f) * (1f - Smooth((age - .08f) / .38f));
                glow *= .50f * EdgeFade(p, .02f);
                for (int k = 0; k < HazeOffsets.Length; k++)
                    mesh.AddVert(p + across * HazeOffsets[k],
                        new Color(SeamGold.r, SeamGold.g, SeamGold.b, glow * HazeAlphas[k]), Vector2.zero);
            }
            int w = HazeOffsets.Length;
            for (int i = 0; i < steps; i++)
                for (int k = 0; k < w - 1; k++)
                {
                    int a = first + i * w + k, b = a + w;
                    mesh.AddTriangle(a, a + 1, b + 1); mesh.AddTriangle(a, b + 1, b);
                }
        }
    }
    private void DrawRimBurst(VertexHelper mesh)
    {
        float age = frameTime - FinaleStart;
        if (age <= 0f || age > .9f || cellRects.Count == 0) return;
        float side = Mathf.Min(bounds.width, bounds.height);
        const int spots = 6;
        for (int k = 0; k < spots; k++)
        {
            float angle = (k + .5f) / spots * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 at = bounds.center + new Vector2(dir.x * bounds.width, dir.y * bounds.height) * .42f;
            Color hue = cellColors[(k * 7) % cellColors.Count];
            CellLightWash.Sparkles(mesh, at, side * .16f, hue, age - (k % 3) * .04f, 3, k * 11 + 40, Vector2.zero, 1.6f);
        }
    }
    // One warm bloom over the whole board once the sweep has crossed it.
    private void DrawBloom(VertexHelper mesh)
    {
        float k = (frameTime - FinaleStart) / .34f;
        if (k <= 0f || k >= 1f) return;
        float pulse = Smooth(k / .25f) * (1f - Smooth((k - .25f) / .75f));
        Vector2 c = bounds.center;
        float r = Mathf.Max(bounds.width, bounds.height) * .62f;
        Grid(mesh, 14, (p, d) =>
            pulse * (.08f + .10f * (1f - Mathf.Clamp01((p - c).magnitude / r))) * EdgeFade(p, .015f),
            new Color(1f, .97f, .88f));
    }
    // Sparkles bursting outward from the board rim as the bloom peaks.
    private void DrawFinaleSparks(VertexHelper mesh)
    {
        const int count = 12;
        for (int i = 0; i < count; i++)
        {
            float h = Hash(i * 5.3f + 1f), h2 = Hash(i * 9.1f + 4f);
            float start = FinaleStart + .04f + h * .14f, life = .30f + h2 * .22f;
            float p = (frameTime - start) / life;
            if (p <= 0f || p >= 1f) continue;
            float angle = (i + h * .6f) / count * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 rim = bounds.center + new Vector2(dir.x * bounds.width, dir.y * bounds.height) * .47f;
            float side = Mathf.Min(bounds.width, bounds.height);
            Vector2 pos = rim + dir * side * (.02f + .16f * Ease(p)) + Vector2.down * side * .05f * p * p;
            float alpha = Mathf.Sin(Mathf.PI * p) * (.7f + .3f * h2);
            float size = side * (.05f + .05f * h) * (1f - .35f * p);
            Color tint = SparkTints[i % SparkTints.Length];
            QuadAt(mesh, new Rect(pos - Vector2.one * size * .5f, Vector2.one * size), FlareUV, alpha, tint);
        }
    }
    private float EdgeFade(Vector2 p, float margin)
    {
        float x = Mathf.Min(p.x - bounds.xMin, bounds.xMax - p.x) / bounds.width;
        float y = Mathf.Min(p.y - bounds.yMin, bounds.yMax - p.y) / bounds.height;
        return Smooth(Mathf.Min(x, y) / margin);
    }
    // Vertex-coloured grid over the board; alpha(point, diagonal) per vertex.
    private void Grid(VertexHelper mesh, int n, System.Func<Vector2, float, float> alpha, Color tint)
    {
        int first = mesh.currentVertCount;
        float max = 0f;
        for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
            {
                Vector2 p = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, x / (float)n),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, y / (float)n));
                float a = Mathf.Clamp01(alpha(p, Diagonal(p))); max = Mathf.Max(max, a);
                mesh.AddVert(p, new Color(tint.r, tint.g, tint.b, a), SolidUV);
            }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = first + y * (n + 1) + x;
                mesh.AddTriangle(i, i + 1, i + n + 2); mesh.AddTriangle(i, i + n + 2, i + n + 1);
            }
    }
    private static void QuadAt(VertexHelper mesh, Rect rect, Rect uv, float alpha, Color tint)
    {
        int first = mesh.currentVertCount;
        Color c = new Color(tint.r, tint.g, tint.b, alpha);
        mesh.AddVert(new Vector2(rect.xMin, rect.yMin), c, new Vector2(uv.xMin, uv.yMin));
        mesh.AddVert(new Vector2(rect.xMax, rect.yMin), c, new Vector2(uv.xMax, uv.yMin));
        mesh.AddVert(new Vector2(rect.xMax, rect.yMax), c, new Vector2(uv.xMax, uv.yMax));
        mesh.AddVert(new Vector2(rect.xMin, rect.yMax), c, new Vector2(uv.xMin, uv.yMax));
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
    }
    private static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    private void DrawFlashes(VertexHelper mesh)
    {
        foreach (Node node in nodes)
        {
            float age = frameTime - BoardVictoryTiming.Arrival(Diagonal(node.Point)) - .006f;
            float p = age / (.105f + .055f * Hash(node.Seed));
            if (p <= 0f || p >= 1f) continue;
            float flash = Smooth(p / .09f) * (1f - Smooth((p - .09f) / .91f));
            float width = node.Side * (50f + Hash(node.Seed + 3f) * 44f) / 96f *
                (.65f + .35f * Mathf.Sin(Mathf.PI * p));
            Flash(mesh, node.Point, width, flash);
            for (int i = 0; i < 5; i++)
            {
                float random = Hash(node.Seed + i * 23f), angle = random * Mathf.PI * 2f;
                float radius = (6f + p * (16f + 26f * Hash(node.Seed + i + 9f))) / 96f * node.Side;
                Vector2 point = node.Point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius +
                    Vector2.up * p * node.Side * 10f / 96f;
                float r = (.6f + Hash(node.Seed + i + 12f) * .7f) / 96f * node.Side;
                QuadAt(mesh, new Rect(point - Vector2.one * r * 7f, Vector2.one * r * 14f), FlareUV,
                    flash * (.55f + .45f * random), SparkTints[(i + (int)node.Seed) % SparkTints.Length]);
            }
        }
        // A few small, short-lived travelling glints, never a persistent outline.
        for (int j = 0; j < seams.Count; j++)
        {
            Seam seam = seams[j];
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = Vector2.Lerp(seam.A, seam.B, .12f + i * .25f);
                float age = frameTime - BoardVictoryTiming.Arrival(Diagonal(p)) - .005f;
                if (age <= 0f || age >= .065f) continue;
                float alpha = Mathf.Sin(Mathf.PI * age / .065f) * (.45f + .4f * Hash(j * 19f + i));
                float width = seam.Side * (11f + Hash(j * 11f + i) * 14f) / 96f;
                Flash(mesh, p, width, alpha);
            }
        }
    }
    private void Flash(VertexHelper mesh, Vector2 centre, float width, float alpha) =>
        SpriteQuad(mesh, new Rect(centre - Vector2.one * width * .5f, Vector2.one * width), FlareUV, alpha, alpha, false);
    private void SpriteQuad(VertexHelper mesh, Rect rect, Rect uv, float alphaA, float alphaB, bool vertical)
    {
        float left = Mathf.Max(rect.xMin, bounds.xMin), right = Mathf.Min(rect.xMax, bounds.xMax);
        float bottom = Mathf.Max(rect.yMin, bounds.yMin), top = Mathf.Min(rect.yMax, bounds.yMax);
        if (right <= left || top <= bottom) return;
        int first = mesh.currentVertCount;
        Vertex(mesh, new Vector2(left, bottom), rect, uv, alphaA, alphaB, vertical);
        Vertex(mesh, new Vector2(right, bottom), rect, uv, alphaA, alphaB, vertical);
        Vertex(mesh, new Vector2(right, top), rect, uv, alphaA, alphaB, vertical);
        Vertex(mesh, new Vector2(left, top), rect, uv, alphaA, alphaB, vertical);
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
    }
    private static void Vertex(VertexHelper mesh, Vector2 p, Rect rect, Rect uv, float a, float b, bool vertical)
    {
        float x = Mathf.InverseLerp(rect.xMin, rect.xMax, p.x), y = Mathf.InverseLerp(rect.yMin, rect.yMax, p.y);
        float u = vertical ? y : x, v = vertical ? x : y;
        mesh.AddVert(p, new Color(1f, 1f, 1f, Mathf.Lerp(a, b, u)),
            new Vector2(uv.xMin + uv.width * u, uv.yMin + uv.height * v));
    }
    private static float Smooth(float p) { p = Mathf.Clamp01(p); return p * p * (3f - 2f * p); }
    private static float Hash(float n) { float value = Mathf.Sin(n * 17.176f + 3.1f) * 43758.5453f; return value - Mathf.Floor(value); }
}
