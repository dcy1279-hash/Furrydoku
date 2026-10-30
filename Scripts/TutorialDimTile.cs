using UnityEngine;
using UnityEngine.UI;

// One cell's share of the tutorial dimmer: the tile (cell + half of each gap) minus a
// rounded hole exactly matching the cell, so a spotlight keeps the board's rounded corners.
// The ring around the hole uses frameAlpha; the hole itself uses holeAlpha (0 = spotlit).
[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialDimTile : MaskableGraphic
{
    private const int ArcSteps = 6;
    private Rect tile, hole;
    private float radius, frameAlpha, holeAlpha;

    public void Set(Rect tileRect, Rect holeRect, float cornerRadius, float ringAlpha, float cellAlpha)
    {
        if (tile == tileRect && hole == holeRect && Mathf.Approximately(radius, cornerRadius) &&
            Mathf.Approximately(frameAlpha, ringAlpha) && Mathf.Approximately(holeAlpha, cellAlpha)) return;
        tile = tileRect; hole = holeRect; radius = cornerRadius; frameAlpha = ringAlpha; holeAlpha = cellAlpha;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (frameAlpha <= .001f && holeAlpha <= .001f) return;
        float r = Mathf.Clamp(radius, 0f, Mathf.Min(hole.width, hole.height) * .5f);
        Color ring = new Color(color.r, color.g, color.b, color.a * frameAlpha);
        Color fill = new Color(color.r, color.g, color.b, color.a * holeAlpha);
        // Corner centres: TR, TL, BL, BR; arc k sweeps from k*90° to k*90°+90°.
        Vector2[] centres =
        {
            new Vector2(hole.xMax - r, hole.yMax - r), new Vector2(hole.xMin + r, hole.yMax - r),
            new Vector2(hole.xMin + r, hole.yMin + r), new Vector2(hole.xMax - r, hole.yMin + r)
        };
        Vector2[] tileCorners =
        {
            new Vector2(tile.xMax, tile.yMax), new Vector2(tile.xMin, tile.yMax),
            new Vector2(tile.xMin, tile.yMin), new Vector2(tile.xMax, tile.yMin)
        };
        var perimeter = new Vector2[4 * (ArcSteps + 1)];
        for (int k = 0; k < 4; k++)
            for (int s = 0; s <= ArcSteps; s++)
            {
                float a = (k * 90f + s * 90f / ArcSteps) * Mathf.Deg2Rad;
                perimeter[k * (ArcSteps + 1) + s] = centres[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }

        // Ring: per corner a fan from the tile corner, plus a strip along each side.
        for (int k = 0; k < 4; k++)
        {
            Vector2 corner = tileCorners[k];
            Vector2 arcStart = perimeter[k * (ArcSteps + 1)], arcEnd = perimeter[k * (ArcSteps + 1) + ArcSteps];
            // Points where the arc's ends meet the tile edges (horizontal/vertical projections).
            bool startVertical = k % 2 == 0; // TR/BL arcs start on a vertical side
            Vector2 edgeA = startVertical ? new Vector2(corner.x, arcStart.y) : new Vector2(arcStart.x, corner.y);
            Vector2 edgeB = startVertical ? new Vector2(arcEnd.x, corner.y) : new Vector2(corner.x, arcEnd.y);
            int c0 = mesh.currentVertCount;
            mesh.AddVert(corner, ring, Vector2.zero);
            mesh.AddVert(edgeA, ring, Vector2.zero);
            for (int s = 0; s <= ArcSteps; s++) mesh.AddVert(perimeter[k * (ArcSteps + 1) + s], ring, Vector2.zero);
            mesh.AddVert(edgeB, ring, Vector2.zero);
            for (int v = 1; v < ArcSteps + 3; v++) mesh.AddTriangle(c0, c0 + v, c0 + v + 1);
            // Side strip from this arc's end to the next arc's start.
            Vector2 nextStart = perimeter[((k + 1) % 4) * (ArcSteps + 1)];
            Vector2 nextCorner = tileCorners[(k + 1) % 4];
            bool nextVertical = (k + 1) % 2 == 0;
            Vector2 nextEdge = nextVertical ? new Vector2(nextCorner.x, nextStart.y) : new Vector2(nextStart.x, nextCorner.y);
            Quad(mesh, arcEnd, edgeB, nextEdge, nextStart, ring);
        }

        // Hole fill (convex): fan from the centre.
        if (holeAlpha > .001f)
        {
            int c0 = mesh.currentVertCount;
            mesh.AddVert(hole.center, fill, Vector2.zero);
            for (int i = 0; i < perimeter.Length; i++) mesh.AddVert(perimeter[i], fill, Vector2.zero);
            for (int i = 0; i < perimeter.Length; i++)
                mesh.AddTriangle(c0, c0 + 1 + i, c0 + 1 + (i + 1) % perimeter.Length);
        }
    }

    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
    }
}
