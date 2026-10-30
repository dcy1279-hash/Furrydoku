using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RuleConflictGraphic : MaskableGraphic
{
    private readonly struct Edge : IEquatable<Edge>
    {
        public readonly Vector2 A, B;
        public Edge(Vector2 a, Vector2 b) { A = a; B = b; }
        public bool Equals(Edge other) => A.Equals(other.A) && B.Equals(other.B);
        public override bool Equals(object other) => other is Edge edge && Equals(edge);
        public override int GetHashCode() => A.GetHashCode() * 397 ^ B.GetHashCode();
    }
    private readonly List<Edge> edges = new List<Edge>(400);
    private readonly List<Rect> faces = new List<Rect>(100);
    private float flash, thickness;
    public void Configure(Rect[] rects, int size, RuleConflictInfo info, float boardWidth)
    {
        edges.Clear(); faces.Clear(); thickness = Mathf.Max(1.5f, boardWidth * .0055f);
        if (rects == null || info == null || rects.Length != size * size) { SetFlash(0f); return; }
        var unique = new HashSet<Edge>(); var filled = new bool[rects.Length];
        foreach (bool[] group in info.Groups)
        {
            if (group == null || group.Length != rects.Length) continue;
            for (int i = 0; i < group.Length; i++)
            {
                if (!group[i]) continue;
                filled[i] = true; int row = i / size, col = i % size; Rect cell = rects[i];
                float left = col == 0 ? cell.xMin : (rects[i - 1].xMax + cell.xMin) * .5f;
                float right = col == size - 1 ? cell.xMax : (cell.xMax + rects[i + 1].xMin) * .5f;
                float top = row == 0 ? cell.yMax : (rects[i - size].yMin + cell.yMax) * .5f;
                float bottom = row == size - 1 ? cell.yMin : (cell.yMin + rects[i + size].yMax) * .5f;
                if (col == 0 || !group[i - 1]) Add(unique, new Vector2(left, bottom), new Vector2(left, top));
                if (col == size - 1 || !group[i + 1]) Add(unique, new Vector2(right, bottom), new Vector2(right, top));
                if (row == 0 || !group[i - size]) Add(unique, new Vector2(left, top), new Vector2(right, top));
                if (row == size - 1 || !group[i + size]) Add(unique, new Vector2(left, bottom), new Vector2(right, bottom));
            }
        }
        for (int i = 0; i < filled.Length; i++) if (filled[i]) faces.Add(rects[i]);
        SetFlash(0f);
    }
    private void Add(HashSet<Edge> unique, Vector2 a, Vector2 b)
    {
        var edge = new Edge(a, b); if (unique.Add(edge)) edges.Add(edge);
    }
    public void SetFlash(float value) { flash = Mathf.Clamp01(value); SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if (flash < .001f) return;
        var red = new Color(1f, .17f, .22f, flash * .94f);
        var tint = new Color(1f, .12f, .17f, flash * .075f);
        foreach (Rect r in faces) Quad(mesh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), tint);
        foreach (Edge edge in edges)
        {
            Vector2 n = new Vector2(-(edge.B - edge.A).y, (edge.B - edge.A).x).normalized * thickness * .5f;
            Quad(mesh, edge.A - n, edge.A + n, edge.B + n, edge.B - n, red);
            Cap(mesh, edge.A, thickness * .5f, red); Cap(mesh, edge.B, thickness * .5f, red);
        }
    }
    private static void Cap(VertexHelper mesh, Vector2 p, float radius, Color tint)
    {
        int first = mesh.currentVertCount; mesh.AddVert(p, tint, Vector2.zero);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * .25f;
            mesh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < 8; i++) mesh.AddTriangle(first, first + i + 1, first + (i + 1) % 8 + 1);
    }
    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
        mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
    }
}
