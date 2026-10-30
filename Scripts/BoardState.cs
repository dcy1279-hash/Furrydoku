using System;

// 棋盘的数据记录，不是需要挂到对象上的 Unity 组件。
public class BoardState
{
    private readonly CellState[] cells;

    public int Size { get; }
    public int CellCount => cells.Length;
    public int RemainingLives { get; private set; }
    public int PlacedCount { get; private set; }

    public bool IsWon => PlacedCount == Size;
    public bool IsFailed => RemainingLives == 0;
    public bool IsFinished => IsWon || IsFailed;

    // 开始新挑战时创建一个新的 BoardState。
    public BoardState(int size, int initialLives = 3)
    {
        if (size < 4 || size > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size), "棋盘尺寸必须在 4 到 10 之间。");
        }

        if (initialLives < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialLives), "初始生命必须至少为 1。");
        }

        Size = size;
        RemainingLives = initialLives;
        cells = new CellState[size * size];

        // CellState.Empty 的值为 0，新数组默认全部是空白。
    }

    public int[] ExportCells()
    {
        var snapshot = new int[cells.Length];
        for (int i = 0; i < cells.Length; i++) snapshot[i] = (int)cells[i];
        return snapshot;
    }

    // 只恢复经校验的快照，不模拟点击/扣命，也不触发通关事件。
    public static bool TryRestore(int size, int[] states, int lives,
        out BoardState restored, out string error)
    {
        restored = null;
        error = null;
        if (size < 4 || size > 10 || states == null || states.Length != size * size ||
            lives < 0 || lives > 3)
        {
            error = "存档棋盘尺寸、格子数量或生命无效。";
            return false;
        }
        int placed = 0, rejected = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] < (int)CellState.Empty || states[i] > (int)CellState.Rejected)
            {
                error = "存档包含未知格子状态。";
                return false;
            }
            if (states[i] == (int)CellState.Placed) placed++;
            if (states[i] == (int)CellState.Rejected) rejected++;
        }
        if (placed > size || rejected > 3 || lives != 3 - rejected ||
            (placed == size && lives == 0))
        {
            error = "存档动物、红叉与生命数量不一致。";
            return false;
        }
        var state = new BoardState(size);
        for (int i = 0; i < states.Length; i++) state.cells[i] = (CellState)states[i];
        state.PlacedCount = placed;
        state.RemainingLives = lives;
        restored = state;
        return true;
    }

    // 读取某个格子的状态，不通过图片是否显示来猜测状态。
    public CellState GetCellState(int index)
    {
        ValidateIndex(index);
        return cells[index];
    }

    // 正确动物和错误红 X 都是锁定格。
    public bool IsCellLocked(int index)
    {
        ValidateIndex(index);
        return cells[index] == CellState.Placed ||
               cells[index] == CellState.Rejected;
    }

    // 普通单击：空白与普通 X 互相切换。
    // 返回 true 表示数据确实改变，需要刷新这个格子的显示。
    public bool TryToggleMark(int index)
    {
        if (!CanChangeCell(index))
        {
            return false;
        }

        cells[index] = cells[index] == CellState.Empty
            ? CellState.Marked
            : CellState.Empty;

        return true;
    }

    // 接收控制器核对关卡答案后的结果，不在这里猜测正确位置。
    // 空白和普通 X 都可尝试；锁定格或已结束挑战不会再次扣命。
    public bool TryResolvePlacement(int index, bool isCorrect)
    {
        if (!CanChangeCell(index))
        {
            return false;
        }

        if (isCorrect)
        {
            cells[index] = CellState.Placed;
            PlacedCount++;
        }
        else
        {
            cells[index] = CellState.Rejected;
            RemainingLives--;
        }

        return true;
    }

    private bool CanChangeCell(int index)
    {
        ValidateIndex(index);
        return !IsFinished && !IsCellLocked(index);
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= cells.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), "格子编号超出棋盘范围。");
        }
    }
}
