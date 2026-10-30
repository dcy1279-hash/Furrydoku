using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 棋盘最上层的一张 UI 网格，只负责边线，不接收点击，也不占 Grid 的格子位置。
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement))]
public class RegionBorderView : MaskableGraphic
{
    [SerializeField, Min(1f)] private float regionLineWidth = 10f;
    [SerializeField, Min(1f)] private float outerLineWidth = 12f;
    [SerializeField] private Color regionLineColor = new Color(.18f, .21f, .26f, 1f);

    [NonSerialized] private RectTransform[] cells;
    [NonSerialized] private RegionBorderGeometry.Segment[] segments;
    [NonSerialized] private float[] columns;
    [NonSerialized] private float[] rows;
    private int size;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly UIVertex[] quad = new UIVertex[4];

    public static bool TryAttach(
        RectTransform board,
        CellView[] views,
        int boardSize,
        IReadOnlyList<int> regionIds,
        out string error)
    {
        error = null;
        if (board == null || views == null || views.Length != boardSize * boardSize)
        {
            error = "区域边界：棋盘或格子数量不正确。";
            return false;
        }

        if (board.Find("RegionBorders") != null)
        {
            error = "区域边界：发现同名子对象，不覆盖已有内容。";
            return false;
        }

        RegionBorderGeometry.Segment[] edges;
        try { edges = RegionBorderGeometry.Build(boardSize, regionIds); }
        catch (ArgumentException exception)
        {
            error = "区域边界：" + exception.Message;
            return false;
        }

        var cellRects = new RectTransform[views.Length];
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null ||
                (cellRects[i] = views[i].transform as RectTransform) == null ||
                cellRects[i].parent != board)
            {
                error = $"区域边界：格子 {i} 不是当前棋盘的直接子对象。";
                return false;
            }
        }

        var overlay = new GameObject(
            "RegionBorders", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(LayoutElement), typeof(RegionBorderView));
        overlay.layer = board.gameObject.layer;
        // 必须先退出自动布局，再放到 Board 下，不能让它变成第 26 个格子。
        overlay.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.SetParent(board, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        RegionBorderView view = overlay.GetComponent<RegionBorderView>();
        view.raycastTarget = false;
        view.cells = cellRects;
        view.segments = edges;
        view.size = boardSize;
        view.columns = new float[boardSize + 1];
        view.rows = new float[boardSize + 1];

        // 等当前 Grid 计算出每个格子的真实位置后再画，不用写死 5×5 坐标。
        LayoutRebuilder.ForceRebuildLayoutImmediate(board);
        view.SetVerticesDirty();
        return true;
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (cells == null || segments == null || columns == null || rows == null ||
            size < 4 || cells.Length != size * size)
            return;

        float minSpan = float.MaxValue;
        // 顶行给出每列左右边缘；第一列给出每行上下边缘。
        float previousRight = 0f;
        for (int column = 0; column < size; column++)
        {
            if (!TryReadRect(cells[column], out Rect cell)) return;
            minSpan = Mathf.Min(minSpan, cell.width);
            columns[column] = column == 0
                ? cell.xMin : (previousRight + cell.xMin) * .5f;
            previousRight = cell.xMax;
        }
        columns[size] = previousRight;

        float previousBottom = 0f;
        for (int row = 0; row < size; row++)
        {
            if (!TryReadRect(cells[row * size], out Rect cell)) return;
            minSpan = Mathf.Min(minSpan, cell.height);
            rows[row] = row == 0
                ? cell.yMax : (previousBottom + cell.yMax) * .5f;
            previousBottom = cell.yMin;
        }
        rows[size] = previousBottom;
        if (minSpan <= 0f) return;

        foreach (RegionBorderGeometry.Segment segment in segments)
        {
            float width = Mathf.Clamp(
                segment.Outer ? outerLineWidth : regionLineWidth,
                1f, Mathf.Max(1f, minSpan * .25f));
            float half = width * .5f;
            Rect line;
            if (segment.Vertical)
            {
                line = new Rect(
                    columns[segment.Line] - half,
                    rows[segment.End] - half,
                    width,
                    rows[segment.Start] - rows[segment.End] + width);
            }
            else
            {
                line = new Rect(
                    columns[segment.Start] - half,
                    rows[segment.Line] - half,
                    columns[segment.End] - columns[segment.Start] + width,
                    width);
            }
            AddRectangle(vertexHelper, line);
        }
    }

    private bool TryReadRect(RectTransform cell, out Rect result)
    {
        result = default;
        if (cell == null) return false;
        cell.GetWorldCorners(corners);
        Vector3 lower = rectTransform.InverseTransformPoint(corners[0]);
        Vector3 upper = rectTransform.InverseTransformPoint(corners[2]);
        result = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        return result.width > 0f && result.height > 0f;
    }

    private void AddRectangle(VertexHelper vertexHelper, Rect line)
    {
        for (int i = 0; i < 4; i++)
        {
            quad[i] = UIVertex.simpleVert;
            quad[i].color = regionLineColor;
            quad[i].uv0 = Vector2.zero;
        }
        quad[0].position = new Vector3(line.xMin, line.yMin);
        quad[1].position = new Vector3(line.xMin, line.yMax);
        quad[2].position = new Vector3(line.xMax, line.yMax);
        quad[3].position = new Vector3(line.xMax, line.yMin);
        vertexHelper.AddUIVertexQuad(quad);
    }
}

