using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform), typeof(GridLayoutGroup))]
public class BoardBuilder : MonoBehaviour
{
    // 保留原字段名和引用；运行时优先采用关卡数据中的大小。
    [SerializeField] private GameObject cellPrefab;
    [SerializeField, Range(4, 10)] private int boardSize = 5;

    [Header("Layout（布局）")]
    [SerializeField, Min(0f)] private float cellSpacing = 8f;
    [SerializeField, Min(0)] private int boardPadding = 8;
    [Tooltip("按参考棋盘比例设置格缝和白边；关闭后使用上面原有手动值。")]
    [SerializeField] private bool proportionalCellGaps = true;

    private bool hasBuilt;
    private int generatedSize;
    [System.NonSerialized] private CellView[] generatedViews;
    private RectTransform boardRect;
    private GridLayoutGroup grid;
    private Vector2 lastRectSize;
    private float lastCellSpacing;
    private int lastBoardPadding;
    private bool layoutWarningShown;
    private bool lastProportionalCellGaps;

    public int BoardSize => hasBuilt ? generatedSize : boardSize;

    // 保留旧调用兼容入口，不用于控制器正式加载。
    public bool TryBuild(out CellView[] views, out CellInput[] inputs) =>
        TryBuild(boardSize, out views, out inputs);

    // 兼容旧单关入口；正式加载使用 TryLoadLevel。
    public bool TryBuild(int levelSize, out CellView[] views, out CellInput[] inputs) =>
        TryCreate(levelSize, null, false, out views, out inputs);

    public bool TryLoadLevel(LevelData level, out CellView[] views, out CellInput[] inputs)
    {
        views = null;
        inputs = null;
        if (level == null || level.RegionIds == null ||
            level.RegionIds.Count != level.Size * level.Size)
            return false;
        return TryCreate(level.Size, level, true, out views, out inputs);
    }

    // 先在停用的临时容器中准备新格子，再替换自己生成的旧格子。
    // 不扫描名称删除对象，不清空整个 Board，不使用 DestroyImmediate。
    private bool TryCreate(int levelSize, LevelData level, bool allowReplace,
        out CellView[] views, out CellInput[] inputs)
    {
        views = null;
        inputs = null;
        if (!Application.isPlaying || (hasBuilt && !allowReplace))
        {
            Debug.LogError("BoardBuilder：当前入口不允许重复生成。", this);
            return false;
        }
        if (levelSize < 4 || levelSize > 10 || cellPrefab == null || !cellPrefab.activeSelf ||
            cellPrefab.GetComponent<RectTransform>() == null ||
            cellPrefab.GetComponent<CellInput>() == null || cellPrefab.GetComponent<CellView>() == null)
        {
            Debug.LogError("BoardBuilder：尺寸需为4～10，Cell预制体需启用并保留显示/输入组件。", this);
            return false;
        }

        // 首次仍要求空容器；切关仅允许替换本生成器持有的格子。
        if (!hasBuilt && transform.childCount != 0)
        {
            Debug.LogError("BoardBuilder：请使用没有子对象的新 Board。", this);
            return false;
        }
        if (hasBuilt)
        {
            if (generatedViews == null || transform.childCount != generatedViews.Length)
            {
                Debug.LogError("BoardBuilder：原格子所有权不可用或有额外子对象，停止替换以保护场景。", this);
                return false;
            }
            foreach (CellView old in generatedViews)
                if (old == null || old.transform.parent != transform)
                {
                    Debug.LogError("BoardBuilder：原格子已被移动/删除，请停止运行后重新开始。", this);
                    return false;
                }
        }

        boardRect = GetComponent<RectTransform>();
        grid = GetComponent<GridLayoutGroup>();
        Canvas.ForceUpdateCanvases();
        GetLayoutSettings(levelSize, out float initialSpacing, out int initialPadding);
        if (!BoardLayout.TryCalculateCellSide(levelSize, boardRect.rect.width, boardRect.rect.height,
                initialSpacing, initialPadding, out float cellSide))
        {
            Debug.LogError("BoardBuilder：当前容器放不下该尺寸的棋盘。", this);
            return false;
        }

        var staging = new GameObject("BoardCells_Staging", typeof(RectTransform));
        staging.layer = gameObject.layer;
        staging.transform.SetParent(transform.parent, false);
        staging.SetActive(false);
        var newViews = new CellView[levelSize * levelSize];
        var newInputs = new CellInput[levelSize * levelSize];
        try
        {
            for (int row = 0; row < levelSize; row++)
                for (int column = 0; column < levelSize; column++)
                {
                    int index = row * levelSize + column;
                    GameObject cell = Instantiate(cellPrefab, staging.transform, false);
                    cell.name = $"Cell_{row}_{column}";
                    CellView view = cell.GetComponent<CellView>();
                    CellInput input = cell.GetComponent<CellInput>();
                    newViews[index] = view;
                    newInputs[index] = input;
                    view.Initialize(index);
                    input.SetInputEnabled(false);
                    if (!view.enabled || !input.enabled || !view.ApplyCellSize(cellSide) ||
                        (level != null && !view.PrepareForLevel(
                            FirstLevelFactory.GetRegionColor(level.RegionIds[index]))))
                    {
                        Destroy(staging);
                        Debug.LogError($"BoardBuilder：新格子 {index} 配置不正确，保留旧棋盘。", this);
                        return false;
                    }
                    if (level != null) view.ApplyRegionDecoration(level.RegionIds[index]);
                    view.Render(CellState.Empty);
                }
        }
        catch (System.Exception error)
        {
            Destroy(staging);
            Debug.LogException(error, this);
            return false;
        }

        // Destroy 延后到帧末；先移出网格并停用，避免同帧旧格子占位/接收点击。
        if (generatedViews != null)
            foreach (CellView old in generatedViews)
            {
                old.GetComponent<CellInput>().SetInputEnabled(false);
                old.gameObject.SetActive(false);
                old.transform.SetParent(staging.transform, false);
            }
        foreach (CellView view in newViews)
            view.transform.SetParent(transform, false);
        Destroy(staging); // 只销毁临时容器及自己持有的旧运行时格子。

        generatedSize = levelSize;
        boardSize = levelSize;
        generatedViews = newViews;
        hasBuilt = true;
        ApplyLayout(out cellSide);
        LayoutRebuilder.ForceRebuildLayoutImmediate(boardRect);
        views = newViews;
        inputs = newInputs;
        return true;
    }

    // 容器大小或间距发生变化时只重新排版，不重新创建格子或清空玩法。
    public bool RefreshLayout()
    {
        if (!Application.isPlaying || !hasBuilt || generatedViews == null)
            return false;

        if (!ApplyLayout(out float cellSide))
        {
            if (!layoutWarningShown)
                Debug.LogWarning("BoardBuilder：当前容器或间距放不下棋盘，保留上一次布局；请调小间距/边距。", this);
            layoutWarningShown = true;
            return false;
        }

        layoutWarningShown = false;
        foreach (CellView view in generatedViews)
            if (view != null && !view.ApplyCellSize(cellSide))
                return false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(boardRect);
        return true;
    }

    private bool ApplyLayout(out float cellSide)
    {
        cellSide = 0f;
        if (boardRect == null || grid == null)
            return false;

        GetLayoutSettings(generatedSize, out float spacing, out int padding);
        if (!BoardLayout.TryCalculateCellSide(
                generatedSize, boardRect.rect.width, boardRect.rect.height,
                spacing, padding, out cellSide))
            return false;

        grid.padding = new RectOffset(padding, padding, padding, padding);
        grid.spacing = new Vector2(spacing, spacing);
        grid.cellSize = new Vector2(cellSide, cellSide);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = generatedSize;
        lastRectSize = boardRect.rect.size;
        lastCellSpacing = cellSpacing;
        lastBoardPadding = boardPadding;
        lastProportionalCellGaps = proportionalCellGaps;
        BoardArtView.Apply(boardRect);
        return true;
    }

    private void GetLayoutSettings(int size, out float spacing, out int padding)
    {
        float side = boardRect != null ? Mathf.Min(boardRect.rect.width, boardRect.rect.height) : 0f;
        if (proportionalCellGaps && side > 0f)
            BoardArtStyle.CalculateGrid(size, side, out spacing, out padding);
        else { spacing = Mathf.Max(0f, cellSpacing); padding = Mathf.Max(0, boardPadding); }
    }

    private void LateUpdate()
    {
        if (!hasBuilt || generatedViews == null || boardRect == null || grid == null)
            return;

        if (boardRect.rect.size != lastRectSize ||
            cellSpacing != lastCellSpacing || boardPadding != lastBoardPadding ||
            proportionalCellGaps != lastProportionalCellGaps)
            RefreshLayout();
    }
}
