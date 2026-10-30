using System;

// 纯布局计算，不创建关卡、不判断动物、不修改游戏状态。
public static class BoardLayout
{
    public static bool TryCalculateCellSide(
        int size, float width, float height,
        float spacing, int padding, out float cellSide)
    {
        cellSide = 0f;
        if (size < 4 || size > 10 || padding < 0 ||
            !IsFinite(width) || !IsFinite(height) || !IsFinite(spacing) ||
            width <= 0f || height <= 0f || spacing < 0f)
            return false;

        float side = Math.Min(width, height);
        float available = side - padding * 2f - (size - 1) * spacing;
        if (available <= 0f) return false;
        cellSide = available / size;
        return IsFinite(cellSide) && cellSide > 0f;
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
