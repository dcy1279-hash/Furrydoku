using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Placement celebrates only its OWN CELL. A fully marked colour celebrates
// the SAME REGION. Reads public region IDs and cell geometry,
// never solution positions, score, lives or saved progress. Region completion
// observes stable cell states, without changing them or replaying restored regions.
public sealed class CorrectPlacementFeedback : MonoBehaviour
{
    public const float Duration = .62f;
    public const float CompletionDuration = .74f;
    public sealed class Edge { public Vector2 A, B; public float Delay; }
    public sealed class Burst
    {
        public bool Active;
        public float Time, Side;
        public Vector2 Origin;
        public int[] Indices;
        public Rect[] Rects;
        public Color[] Colors;
        public float[] Delays;
        public Edge[] Edges;
        public int Seed;
        public float ScaleLag, ScaleAmount;
        public bool CompletedRegion;
        public int RegionId;
        public float MotionEnd;
    }
    private Burst[] bursts;
    private BoardState observedBoard;
    private readonly HashSet<int> completedRegions = new HashSet<int>();
    private readonly Queue<int> pendingRegions = new Queue<int>();
    private Func<float> lineStartDelay;
    private RectTransform board, overlay;
    private CellView[] cells;
    private int size;
    private int[] regions;
    private float[] scales, previousScales;
    private CorrectPlacementGraphic graphic;
    private Rect originalRect;
    private Matrix4x4 originalMatrix;
    private bool focused = true, paused;
    private int next;
    private float ActiveRemaining
    {
        get
        {
            float r = 0f;
            if (bursts != null) foreach (Burst b in bursts) if (b.Active)
                r = Mathf.Max(r, (b.CompletedRegion ? CompletionDuration : Duration) - b.Time);
            return r;
        }
    }

    public float RegionMotionRemaining
    {
        get
        {
            float remaining = 0f;
            if (bursts != null) foreach (Burst b in bursts)
                if (b.Active && b.CompletedRegion)
                    remaining = Mathf.Max(remaining, b.MotionEnd - b.Time);
            return Mathf.Max(0f, remaining);
        }
    }

    public float Remaining => pendingRegions.Count == 0 ? ActiveRemaining :
        Mathf.Max(ActiveRemaining, Mathf.Max(GetLineDelay(), RegionMotionRemaining) +
            pendingRegions.Count * CompletionDuration);

    public void BindLineSequence(Func<float> delayUntilReady) => lineStartDelay = delayUntilReady;

    private float GetLineDelay() => lineStartDelay != null ? Mathf.Max(0f, lineStartDelay()) : 0f;
    public static CorrectPlacementFeedback Create(RectTransform board, CellView[] cells, LevelData level)
    {
        if (board == null || !(board.parent is RectTransform) || level == null || cells == null || cells.Length != level.Size * level.Size) return null;
        RectTransform root = PropBarView.MakeRect("CorrectRegionGoldFeedback", board.parent);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var feedback = root.gameObject.AddComponent<CorrectPlacementFeedback>();
        feedback.board = board; feedback.overlay = root; feedback.cells = cells; feedback.size = level.Size;
        feedback.regions = new int[cells.Length];
        for (int i = 0; i < cells.Length; i++) feedback.regions[i] = level.RegionIds[i];
        // Three existing placement bursts + one completion slot per colour.
        // Fast hint/drag completion must not overwrite another colour's wave.
        feedback.bursts = new Burst[3 + level.Size];
        for (int i = 0; i < feedback.bursts.Length; i++) feedback.bursts[i] = new Burst();
        feedback.scales = new float[cells.Length]; feedback.previousScales = new float[cells.Length];
        for (int i = 0; i < cells.Length; i++) feedback.previousScales[i] = 1f;
        feedback.graphic = root.gameObject.AddComponent<CorrectPlacementGraphic>();
        feedback.graphic.raycastTarget = false; feedback.graphic.SetBursts(feedback.bursts);
        feedback.focused = Application.isFocused;
        return feedback;
    }
    public void BindCompletionState(BoardState state)
    {
        ResetFeedback();
        observedBoard = state;
        completedRegions.Clear();
        if (state == null || regions == null || state.CellCount != regions.Length) return;
        foreach (int region in regions)
            if (!completedRegions.Contains(region) && IsRegionComplete(region))
                completedRegions.Add(region); // Restore silently; do not replay old progress.
    }

    public void ObserveCompletion(BoardState state, int changedIndex)
    {
        if (state == null || state != observedBoard || state.IsFailed || !isActiveAndEnabled ||
            !focused || paused || changedIndex < 0 || changedIndex >= regions.Length) return;
        int region = regions[changedIndex];
        if (completedRegions.Contains(region) || !IsRegionComplete(region)) return;
        // Register completion silently when the PET was the final missing cell.
        // Only a newly added normal/red cross, with an existing pet, celebrates.
        completedRegions.Add(region);
        CellState changed = state.GetCellState(changedIndex);
        if (changed != CellState.Marked && changed != CellState.Rejected) return;
        // Defer until Update so same-operation row/column feedback goes first.
        pendingRegions.Enqueue(changedIndex);
    }

    private bool IsRegionComplete(int region)
    {
        if (observedBoard == null || observedBoard.CellCount != regions.Length) return false;
        int animals = 0;
        for (int i = 0; i < regions.Length; i++) if (regions[i] == region)
        {
            CellState state = observedBoard.GetCellState(i);
            if (state == CellState.Placed) animals++;
            else if (state != CellState.Marked && state != CellState.Rejected) return false;
        }
        return animals == 1;
    }

    public void Play(int origin) => PlayInternal(origin, false);

    private void BeginPendingRegion()
    {
        if (pendingRegions.Count == 0 || GetLineDelay() > 0f || RegionMotionRemaining > .04f) return;
        while (pendingRegions.Count > 0)
        {
            int origin = pendingRegions.Dequeue();
            if (!IsRegionComplete(regions[origin])) continue;
            if (PlayInternal(origin, true)) return;
        }
    }

    private bool PlayInternal(int origin, bool completed)
    {
        if (!isActiveAndEnabled || origin < 0 || origin >= cells.Length || board == null) return false;
        if (ActiveRemaining > 0f && (board.rect != originalRect || board.localToWorldMatrix != originalMatrix)) ResetFeedback();
        overlay.anchorMin = board.anchorMin; overlay.anchorMax = board.anchorMax;
        overlay.pivot = board.pivot; overlay.sizeDelta = board.sizeDelta;
        overlay.anchoredPosition3D = board.anchoredPosition3D;
        overlay.localScale = board.localScale; overlay.localRotation = board.localRotation;
        overlay.SetSiblingIndex(board.GetSiblingIndex() + 1);
        originalRect = board.rect; originalMatrix = board.localToWorldMatrix;
        var rectangles = new Rect[cells.Length];
        var corners = new Vector3[4];
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || !(cells[i].transform is RectTransform r)) return false;
            r.GetWorldCorners(corners);
            Vector3 a = overlay.InverseTransformPoint(corners[0]), b = overlay.InverseTransformPoint(corners[2]);
            rectangles[i] = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
        // Only region COMPLETION may traverse same-colour neighbours.
        // An ordinary correct placement keeps exactly one cell in its wave.
        int[] distance = new int[cells.Length];
        for (int i = 0; i < distance.Length; i++) distance[i] = -1;
        var queue = new Queue<int>();
        distance[origin] = 0;
        if (completed) queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue(), row = current / size, col = current % size;
            int[] neighbours = { row > 0 ? current - size : -1, row + 1 < size ? current + size : -1,
                col > 0 ? current - 1 : -1, col + 1 < size ? current + 1 : -1 };
            foreach (int n in neighbours)
                if (n >= 0 && distance[n] < 0 && regions[n] == regions[origin])
                { distance[n] = distance[current] + 1; queue.Enqueue(n); }
        }
        var indices = new List<int>(); var delays = new List<float>(); var edges = new List<Edge>();
        var cellRects = new List<Rect>(); var cellColors = new List<Color>();
        var keys = new HashSet<int>();
        int minDiagonal = int.MaxValue, maxDiagonal = int.MinValue;
        if (completed) for (int i = 0; i < cells.Length; i++) if (distance[i] >= 0)
        {
            int diagonal = i % size + size - 1 - i / size; // Lower-left -> upper-right.
            minDiagonal = Mathf.Min(minDiagonal, diagonal);
            maxDiagonal = Mathf.Max(maxDiagonal, diagonal);
        }
        for (int i = 0; i < cells.Length; i++) if (distance[i] >= 0)
        {
            float delay = completed
                ? .015f + .27f * (i % size + size - 1 - i / size - minDiagonal) /
                    Mathf.Max(1, maxDiagonal - minDiagonal)
                : .015f;
            indices.Add(i); delays.Add(delay); cellRects.Add(rectangles[i]); cellColors.Add(cells[i].RegionColor);
            int row = i / size, col = i % size;
            Rect r = rectangles[i];
            float left = !completed || col == 0 ? r.xMin : (rectangles[i - 1].xMax + r.xMin) * .5f;
            float right = !completed || col == size - 1 ? r.xMax : (r.xMax + rectangles[i + 1].xMin) * .5f;
            float top = !completed || row == 0 ? r.yMax : (rectangles[i - size].yMin + r.yMax) * .5f;
            float bottom = !completed || row == size - 1 ? r.yMin : (r.yMin + rectangles[i + size].yMax) * .5f;
            // Unique keys for the actual grid seams, including internal seams.
            AddEdge(keys, edges, row * (size + 1) + col, new Vector2(left, bottom), new Vector2(left, top), delay);
            AddEdge(keys, edges, row * (size + 1) + col + 1, new Vector2(right, bottom), new Vector2(right, top), delay);
            int offset = size * (size + 1);
            AddEdge(keys, edges, offset + row * size + col, new Vector2(left, top), new Vector2(right, top), delay);
            AddEdge(keys, edges, offset + (row + 1) * size + col, new Vector2(left, bottom), new Vector2(right, bottom), delay);
        }
        // Shared seams use the earlier-arriving adjacent cell, not list order.
        Burst burst = null;
        if (completed)
        {
            for (int i = 3; i < bursts.Length; i++)
                if (!bursts[i].Active) { burst = bursts[i]; break; }
            if (burst == null) return false;
        }
        else { burst = bursts[next]; next = (next + 1) % 3; }
        burst.Active = true; burst.Time = 0f; burst.Origin = rectangles[origin].center;
        burst.Side = rectangles[origin].width; burst.Indices = indices.ToArray(); burst.Delays = delays.ToArray(); burst.Rects = cellRects.ToArray(); burst.Colors = cellColors.ToArray();
        burst.Edges = edges.ToArray(); burst.Seed = origin * 31 + 79;
        burst.CompletedRegion = completed; burst.RegionId = regions[origin];
        burst.ScaleLag = completed ? .035f : 0f;
        burst.ScaleAmount = completed ? .06f : .045f;
        burst.MotionEnd = 0f;
        foreach (float delay in burst.Delays)
            burst.MotionEnd = Mathf.Max(burst.MotionEnd, delay + burst.ScaleLag + .23f);
        graphic.SetVerticesDirty();
        // A fully marked colour is silent too (reference has no sound for it).
        return true;
    }
    private static void AddEdge(HashSet<int> keys, List<Edge> edges, int key, Vector2 a, Vector2 b, float delay)
    {
        if (!keys.Add(key))
        {
            foreach (Edge edge in edges)
                if (edge.A == a && edge.B == b) { edge.Delay = Mathf.Min(edge.Delay, delay); break; }
            return;
        }
        edges.Add(new Edge { A = a, B = b, Delay = delay });
    }
    public static float Pulse(float localTime)
    {
        if (localTime <= 0f || localTime >= .23f) return 0f;
        return localTime < .065f ? Mathf.SmoothStep(0f, 1f, localTime / .065f) :
            1f - Mathf.SmoothStep(0f, 1f, (localTime - .065f) / .165f);
    }
    private void Update()
    {
        if (board == null || cells == null) return;
        if (!focused || paused) return;
        if (ActiveRemaining > 0f && (board.rect != originalRect || board.localToWorldMatrix != originalMatrix))
        { ResetFeedback(); return; }
        BeginPendingRegion();
        if (ActiveRemaining <= 0f && graphic.IsEmpty) return;
        for (int i = 0; i < scales.Length; i++) scales[i] = 1f;
        foreach (Burst b in bursts) if (b.Active)
        {
            if (b.CompletedRegion && !IsRegionComplete(b.RegionId))
            { b.Active = false; continue; }
            b.Time += Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (b.Time >= (b.CompletedRegion ? CompletionDuration : Duration)) { b.Active = false; continue; }
            for (int j = 0; j < b.Indices.Length; j++)
                scales[b.Indices[j]] = Mathf.Max(scales[b.Indices[j]],
                    1f + b.ScaleAmount * Pulse(b.Time - b.Delays[j] - b.ScaleLag));
        }
        for (int i = 0; i < cells.Length; i++)
            if (!Mathf.Approximately(scales[i], previousScales[i]))
            { if (cells[i] != null) cells[i].TrySetRegionPulseScale(scales[i]); previousScales[i] = scales[i]; }
        if (ActiveRemaining > 0f || !graphic.IsEmpty) graphic.RefreshFrame(ActiveRemaining <= 0f);
    }
    public void ResetFeedback()
    {
        pendingRegions.Clear();
        if (bursts != null) foreach (Burst b in bursts) b.Active = false;
        if (cells != null) for (int i = 0; i < cells.Length; i++)
        { if (cells[i] != null) cells[i].TrySetRegionPulseScale(1f); previousScales[i] = 1f; }
        if (graphic != null) graphic.RefreshFrame(true);
    }
    private void OnDisable() => ResetFeedback();
    private void OnApplicationFocus(bool value) { focused = value; if (!value) ResetFeedback(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) ResetFeedback(); }
    private void OnDestroy() { ResetFeedback(); lineStartDelay = null; }
}
