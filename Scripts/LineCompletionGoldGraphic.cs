using UnityEngine;
using UnityEngine.UI;

// Only the active completed line. v2: cells light up (CellLightWash) like the reference. Geometry uses cell roots; no reparenting,
// hit-target changes, board-state writes, or independent animation clock.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class LineCompletionGoldGraphic : MaskableGraphic
{
    private const int CornerSteps = 8;
    private const int PerimeterPoints = 4 * (CornerSteps + 1);
    private Rect[] orderedCells;
    private Color[] orderedColors;
    private float elapsed;

    public void Begin(Rect[] cells, Color[] colors)
    {
        orderedCells = cells;
        orderedColors = colors;
        elapsed = 0f;
        SetVerticesDirty();
    }

    public void SetTime(float seconds)
    {
        if (orderedCells == null) return;
        elapsed = seconds;
        SetVerticesDirty();
    }

    public void Clear()
    {
        orderedCells = null;
        elapsed = 0f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (orderedCells == null) return;
        for (int step = 0; step < orderedCells.Length; step++)
        {
            float age = elapsed - step * LineCompletionFeedback.CellWaveStagger;
            float strength = CellLightWash.Envelope(age);
            if (strength <= 0f) continue;
            Rect cell = orderedCells[step];
            // Follow the cell's own 1 -> 1.06 -> 1 pop while it is scaling.
            if (age < LineCompletionFeedback.CellWaveDuration)
            {
                float pulse = Mathf.Sin(age / LineCompletionFeedback.CellWaveDuration * Mathf.PI);
                Vector2 side = cell.size * (1f + (LineCompletionFeedback.CellWaveMaxScale - 1f) * pulse);
                cell = new Rect(cell.center - side * .5f, side);
            }
            // Reference look: the cell turns a paler version of its own colour with a same-hue glow.
            Color hue = orderedColors != null && step < orderedColors.Length ? orderedColors[step] : Color.white;
            CellLightWash.Draw(mesh, cell, BoardArtStyle.CellCornerRatio, strength, hue, Vector2.zero);
            CellLightWash.Sparkles(mesh, cell.center, Mathf.Min(cell.width, cell.height), hue, age, 2, step * 5 + 1, Vector2.zero);
        }
    }

    private static float Hash(int v) { float x = Mathf.Sin(v * 12.9898f + 78.233f) * 43758.5453f; return x - Mathf.Floor(x); }
    private static void Spot(VertexHelper mesh, Vector2 point, float radius, Color c)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(point, c, Vector2.zero);
        Color clear = new Color(c.r, c.g, c.b, 0f);
        for (int j = 0; j < 16; j++)
        { float a = j * Mathf.PI / 8f; mesh.AddVert(point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, clear, Vector2.zero); }
        for (int j = 0; j < 16; j++) mesh.AddTriangle(first, first + 1 + j, first + 1 + (j + 1) % 16);
    }
    private static void Star(VertexHelper mesh, Vector2 p, float r, Color c)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(p + Vector2.up * r, c, Vector2.zero); mesh.AddVert(p + Vector2.right * r * .22f, c, Vector2.zero);
        mesh.AddVert(p + Vector2.down * r, c, Vector2.zero); mesh.AddVert(p + Vector2.left * r * .22f, c, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        i = mesh.currentVertCount;
        mesh.AddVert(p + Vector2.right * r, c, Vector2.zero); mesh.AddVert(p + Vector2.down * r * .22f, c, Vector2.zero);
        mesh.AddVert(p + Vector2.left * r, c, Vector2.zero); mesh.AddVert(p + Vector2.up * r * .22f, c, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Ring(VertexHelper mesh, Rect rect, float radius,
        float inner, float outer, Color innerColor, Color outerColor)
    {
        int start = mesh.currentVertCount;
        for (int p = 0; p < PerimeterPoints; p++)
        {
            mesh.AddVert(Point(rect, radius, inner, p), innerColor, Vector2.zero);
            mesh.AddVert(Point(rect, radius, outer, p), outerColor, Vector2.zero);
        }
        for (int p = 0; p < PerimeterPoints; p++)
        {
            int a = start + p * 2;
            int b = start + ((p + 1) % PerimeterPoints) * 2;
            mesh.AddTriangle(a, a + 1, b + 1);
            mesh.AddTriangle(a, b + 1, b);
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

    protected override void OnDisable()
    {
        Clear();
        base.OnDisable();
    }
}
