using System;
using System.Collections.Generic;

// 行/列编号和格子编号均从 0 开始；日志由控制器转换为从 1 开始。
public enum PuzzleLineAxis
{
    Row,
    Column
}

// 留给后续 Good/涟漪表现的通知数据，不负责决定动画方向。
public readonly struct LineCompletionInfo
{
    public PuzzleLineAxis Axis { get; }
    public int LineIndex { get; }
    public int AnimalCellIndex { get; }
    public int ChangedCellIndex { get; }

    public LineCompletionInfo(
        PuzzleLineAxis axis, int lineIndex, int animalCellIndex, int changedCellIndex)
    {
        Axis = axis;
        LineIndex = lineIndex;
        AnimalCellIndex = animalCellIndex;
        ChangedCellIndex = changedCellIndex;
    }
}

// 普通 C# 检测器，不挂对象，不修改格子、生命或胜负。
public sealed class LineCompletionTracker
{
    private readonly int size;
    private readonly bool[] reportedRows;
    private readonly bool[] reportedColumns;

    public LineCompletionTracker(int boardSize)
    {
        if (boardSize < 4 || boardSize > 10)
            throw new ArgumentOutOfRangeException(nameof(boardSize));
        size = boardSize;
        reportedRows = new bool[size];
        reportedColumns = new bool[size];
    }

    public bool[] ExportRows() => (bool[])reportedRows.Clone();
    public bool[] ExportColumns() => (bool[])reportedColumns.Clone();

    public static bool TryRestore(BoardState board, bool[] rows, bool[] columns,
        out LineCompletionTracker restored, out string error)
    {
        restored = null;
        error = null;
        if (board == null || rows == null || columns == null ||
            rows.Length != board.Size || columns.Length != board.Size)
        {
            error = "存档行列通知记录长度无效。";
            return false;
        }
        var tracker = new LineCompletionTracker(board.Size);
        for (int i = 0; i < board.Size; i++)
        {
            if (!tracker.ValidReportedFlag(board, PuzzleLineAxis.Row, i, rows[i]) ||
                !tracker.ValidReportedFlag(board, PuzzleLineAxis.Column, i, columns[i]))
            {
                error = "存档行列通知记录与动物/排除状态不一致。";
                return false;
            }
            tracker.reportedRows[i] = rows[i];
            tracker.reportedColumns[i] = columns[i];
        }
        restored = tracker;
        return true;
    }

    private bool ValidReportedFlag(BoardState board, PuzzleLineAxis axis, int line, bool reported)
    {
        if (!reported) return !IsComplete(board, axis, line, out int ignored);
        // 普通叉可能后来取消，历史通知仍需保留；正确动物锁定，不会消失。
        for (int offset = 0; offset < size; offset++)
        {
            int index = axis == PuzzleLineAxis.Row ? line * size + offset : offset * size + line;
            if (board.GetCellState(index) == CellState.Placed) return true;
        }
        return false;
    }

    // 只在稳定状态改变后调用；一次格子变化最多影响该行和该列。
    // 标记已通知后再交给控制器派发，取消普通叉再补回也不会重复触发。
    public void CollectNewCompletions(
        BoardState board, int changedCellIndex, List<LineCompletionInfo> results)
    {
        if (board == null) throw new ArgumentNullException(nameof(board));
        if (results == null) throw new ArgumentNullException(nameof(results));
        if (board.Size != size)
            throw new ArgumentException("检测器尺寸与棋盘不一致。", nameof(board));
        if (changedCellIndex < 0 || changedCellIndex >= board.CellCount)
            throw new ArgumentOutOfRangeException(nameof(changedCellIndex));

        results.Clear();
        int row = changedCellIndex / size;
        int column = changedCellIndex % size;

        if (!reportedRows[row] &&
            IsComplete(board, PuzzleLineAxis.Row, row, out int rowAnimal))
        {
            reportedRows[row] = true;
            results.Add(new LineCompletionInfo(
                PuzzleLineAxis.Row, row, rowAnimal, changedCellIndex));
        }

        if (!reportedColumns[column] &&
            IsComplete(board, PuzzleLineAxis.Column, column, out int columnAnimal))
        {
            reportedColumns[column] = true;
            results.Add(new LineCompletionInfo(
                PuzzleLineAxis.Column, column, columnAnimal, changedCellIndex));
        }
    }

    private bool IsComplete(
        BoardState board, PuzzleLineAxis axis, int lineIndex, out int animalIndex)
    {
        animalIndex = -1;
        for (int offset = 0; offset < size; offset++)
        {
            int index = axis == PuzzleLineAxis.Row
                ? lineIndex * size + offset
                : offset * size + lineIndex;

            switch (board.GetCellState(index))
            {
                case CellState.Placed:
                    // 必须恰好一个已正确确认的动物，不接受纯叉或多个动物的线。
                    if (animalIndex >= 0) return false;
                    animalIndex = index;
                    break;
                case CellState.Marked:
                case CellState.Rejected:
                    // 普通叉和锁定红叉都算已排除。
                    break;
                default:
                    return false;
            }
        }
        return animalIndex >= 0;
    }
}
