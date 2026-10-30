using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Shared dimmer for the tutorial and the hint overlay: darkens the whole screen except the
// spotlit cells, each revealed with the board's own rounded corners. Tiles never overlap
// (no dark seams): each cell owns itself plus half of every gap, a ring covers the board
// frame and four strips cover the rest of the screen. Presentation only, never blocks taps.
public sealed class BoardSpotlight : MonoBehaviour
{
    public static readonly Color Dim = new Color(.11f, .09f, .08f, .74f);

    private RectTransform root;
    private CellView[] views;
    private int size;
    private Image[] screenDims, ringDims;
    private readonly List<TutorialDimTile> cellDims = new List<TutorialDimTile>(100);
    private float[] frameAlpha, holeAlpha;
    private bool[] spot;
    private bool dimmed;
    private readonly Vector3[] corners = new Vector3[4];

    public Rect BoardRect { get; private set; }
    public float CellSide { get; private set; } = 100f;

    public static BoardSpotlight Create(RectTransform parent)
    {
        RectTransform r = PropBarView.MakeRect("BoardSpotlight", parent);
        PropBarView.Stretch(r);
        var light = r.gameObject.AddComponent<BoardSpotlight>();
        light.root = r;
        light.screenDims = new Image[4]; light.ringDims = new Image[4];
        for (int i = 0; i < 4; i++) light.screenDims[i] = light.DimImage("ScreenDim" + i);
        for (int i = 0; i < 4; i++) light.ringDims[i] = light.DimImage("FrameDim" + i);
        return light;
    }

    public void Bind(CellView[] cells)
    {
        views = cells;
        size = cells != null ? Mathf.RoundToInt(Mathf.Sqrt(cells.Length)) : 0;
        int count = size * size;
        while (cellDims.Count < count)
        {
            RectTransform r = PropBarView.MakeRect("CellDim" + cellDims.Count, root);
            PropBarView.Stretch(r);
            var tile = r.gameObject.AddComponent<TutorialDimTile>();
            tile.color = new Color(Dim.r, Dim.g, Dim.b, 1f); tile.raycastTarget = false;
            cellDims.Add(tile);
        }
        for (int i = 0; i < cellDims.Count; i++) cellDims[i].gameObject.SetActive(i < count);
        frameAlpha = new float[count]; holeAlpha = new float[count]; spot = new bool[count];
    }

    public void SetDimmed(bool on) => dimmed = on;

    public void SpotOnly(IEnumerable<int> cells)
    {
        if (spot == null) return;
        System.Array.Clear(spot, 0, spot.Length);
        if (cells != null) foreach (int i in cells) if (i >= 0 && i < spot.Length) spot[i] = true;
    }

    // Jump straight to the target alpha (e.g. when an overlay opens) instead of fading in.
    public void Snap()
    {
        if (spot == null) return;
        for (int i = 0; i < spot.Length; i++)
        { frameAlpha[i] = dimmed ? Dim.a : 0f; holeAlpha[i] = dimmed && !spot[i] ? Dim.a : 0f; }
        foreach (Image image in screenDims) SetAlpha(image, dimmed ? Dim.a : 0f);
        foreach (Image image in ringDims) SetAlpha(image, dimmed ? Dim.a : 0f);
    }

    public Rect CellRect(int index)
    {
        ((RectTransform)views[index].transform).GetWorldCorners(corners);
        Vector2 a = root.InverseTransformPoint(corners[0]), b = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    public bool Measure()
    {
        if (views == null || size == 0) return false;
        Rect board = default;
        for (int i = 0; i < size * size; i++)
        {
            if (views[i] == null) return false;
            Rect cell = CellRect(i);
            board = i == 0 ? cell : Rect.MinMaxRect(Mathf.Min(board.xMin, cell.xMin), Mathf.Min(board.yMin, cell.yMin),
                Mathf.Max(board.xMax, cell.xMax), Mathf.Max(board.yMax, cell.yMax));
            if (i == 0) CellSide = Mathf.Min(cell.width, cell.height);
        }
        BoardRect = board;
        return true;
    }

    private void LateUpdate()
    {
        if (!Measure()) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        int n = size;
        float halfGap = n > 1 ? Mathf.Max(0f, (CellRect(1).xMin - CellRect(0).xMax) * .5f) : 0f;
        var xs = new float[n + 1]; var ys = new float[n + 1];
        for (int k = 0; k <= n; k++)
        {
            Rect left = CellRect(Mathf.Max(0, k - 1)), right = CellRect(Mathf.Min(n - 1, k));
            Rect above = CellRect(Mathf.Max(0, k - 1) * n), below = CellRect(Mathf.Min(n - 1, k) * n);
            xs[k] = k == 0 ? right.xMin - halfGap : k == n ? left.xMax + halfGap : (left.xMax + right.xMin) * .5f;
            ys[k] = k == 0 ? below.yMax + halfGap : k == n ? above.yMin - halfGap : (above.yMin + below.yMax) * .5f;
        }
        Rect inner = Rect.MinMaxRect(xs[0], ys[n], xs[n], ys[0]);
        float margin = CellSide * .3f;
        Rect frame = Rect.MinMaxRect(inner.xMin - margin, inner.yMin - margin, inner.xMax + margin, inner.yMax + margin);
        Rect screen = root.rect;
        Place(screenDims[0], Rect.MinMaxRect(screen.xMin, frame.yMax, screen.xMax, screen.yMax), dt);
        Place(screenDims[1], Rect.MinMaxRect(screen.xMin, screen.yMin, screen.xMax, frame.yMin), dt);
        Place(screenDims[2], Rect.MinMaxRect(screen.xMin, frame.yMin, frame.xMin, frame.yMax), dt);
        Place(screenDims[3], Rect.MinMaxRect(frame.xMax, frame.yMin, screen.xMax, frame.yMax), dt);
        Place(ringDims[0], Rect.MinMaxRect(frame.xMin, inner.yMax, frame.xMax, frame.yMax), dt);
        Place(ringDims[1], Rect.MinMaxRect(frame.xMin, frame.yMin, frame.xMax, inner.yMin), dt);
        Place(ringDims[2], Rect.MinMaxRect(frame.xMin, inner.yMin, inner.xMin, inner.yMax), dt);
        Place(ringDims[3], Rect.MinMaxRect(inner.xMax, inner.yMin, frame.xMax, inner.yMax), dt);
        for (int i = 0; i < n * n; i++)
        {
            int row = i / n, col = i % n;
            Rect cell = CellRect(i);
            // A pressed tile shrinks; shrink the hole with it so no undimmed board shows around it.
            float squash = views[i] != null ? Mathf.Min(1f, views[i].VisualScale) : 1f;
            if (squash < .999f)
                cell = new Rect(cell.center - cell.size * squash * .5f, cell.size * squash);
            frameAlpha[i] = Mathf.MoveTowards(frameAlpha[i], dimmed ? Dim.a : 0f, dt * 4f);
            holeAlpha[i] = Mathf.MoveTowards(holeAlpha[i], dimmed && !spot[i] ? Dim.a : 0f, dt * 4f);
            // Hole = the cell itself with the board's own corner radius.
            cellDims[i].Set(Rect.MinMaxRect(xs[col], ys[row + 1], xs[col + 1], ys[row]), cell,
                Mathf.Min(cell.width, cell.height) * BoardArtStyle.CellCornerRatio, frameAlpha[i], holeAlpha[i]);
        }
    }

    private Image DimImage(string name)
    {
        RectTransform r = PropBarView.MakeRect(name, root);
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, .5f);
        var image = r.gameObject.AddComponent<Image>();
        image.color = new Color(Dim.r, Dim.g, Dim.b, 0f); image.raycastTarget = false;
        return image;
    }

    private void Place(Image image, Rect rect, float dt)
    {
        RectTransform r = image.rectTransform;
        r.anchoredPosition = rect.center;
        r.sizeDelta = new Vector2(Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        Color c = image.color;
        c.a = Mathf.MoveTowards(c.a, dimmed ? Dim.a : 0f, dt * 4f);
        image.color = c;
    }

    private static void SetAlpha(Image image, float a) { Color c = image.color; c.a = a; image.color = c; }
}
