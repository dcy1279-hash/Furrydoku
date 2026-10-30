using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// White collection tab that grows out of the board plate: rounded top corners, straight
// sides, and concave fillets flaring into the board's top edge (no seam, no capsule gap).
// The RectTransform is the tab body; the fillets are drawn just outside its bottom corners.
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CollectionTabGraphic : MaskableGraphic
{
    private const int ArcSteps = 10;
    private float topRadius = 20f, filletRadius = 16f;
    private readonly List<Vector2> outline = new List<Vector2>(64);

    public void SetShape(float top, float fillet)
    {
        if (Mathf.Approximately(top, topRadius) && Mathf.Approximately(fillet, filletRadius)) return;
        topRadius = top; filletRadius = fillet; SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        float x0 = r.xMin, x1 = r.xMax, y0 = r.yMin, y1 = r.yMax;
        float rt = Mathf.Clamp(topRadius, 0f, Mathf.Min(r.width * .5f, r.height));
        float f = Mathf.Clamp(filletRadius, 0f, r.height - rt);

        // Outline from the left foot, up and over, down to the right foot.
        outline.Clear();
        Arc(new Vector2(x0 - f, y0 + f), f, 270f, 360f, false);   // left fillet (concave)
        Arc(new Vector2(x0 + rt, y1 - rt), rt, 180f, 90f, false); // top-left
        Arc(new Vector2(x1 - rt, y1 - rt), rt, 90f, 0f, false);   // top-right
        Arc(new Vector2(x1 + f, y0 + f), f, 180f, 270f, false);   // right fillet (concave)

        Color fill = color;
        Color clear = new Color(fill.r, fill.g, fill.b, 0f);
        // Fill: the shape is star-shaped from the middle of its bottom edge.
        int c0 = mesh.currentVertCount;
        mesh.AddVert(new Vector2((x0 + x1) * .5f, y0), fill, Vector2.zero);
        for (int i = 0; i < outline.Count; i++) mesh.AddVert(outline[i], fill, Vector2.zero);
        for (int i = 0; i < outline.Count - 1; i++) mesh.AddTriangle(c0, c0 + 1 + i, c0 + 2 + i);

        // ~1 screen-pixel feather along the visible outline (the bottom merges into the board).
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float feather = 1.2f / scale;
        int f0 = mesh.currentVertCount;
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 prev = outline[Mathf.Max(0, i - 1)], next = outline[Mathf.Min(outline.Count - 1, i + 1)];
            Vector2 tangent = (next - prev).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x); // outward for this clockwise-over-the-top path
            mesh.AddVert(outline[i], fill, Vector2.zero);
            mesh.AddVert(outline[i] + normal * feather, clear, Vector2.zero);
        }
        for (int i = 0; i < outline.Count - 1; i++)
        {
            int a = f0 + i * 2, b = a + 2;
            mesh.AddTriangle(a, a + 1, b + 1); mesh.AddTriangle(a, b + 1, b);
        }
    }

    private void Arc(Vector2 centre, float radius, float fromDeg, float toDeg, bool skipFirst)
    {
        for (int s = skipFirst ? 1 : 0; s <= ArcSteps; s++)
        {
            float a = Mathf.Lerp(fromDeg, toDeg, s / (float)ArcSteps) * Mathf.Deg2Rad;
            outline.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }
}
