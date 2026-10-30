using System;
using System.Collections.Generic;

// 纯数据计算：只比较区域编号，不读图片颜色、不修改棋盘状态。
public static class RegionBorderGeometry
{
    public struct Segment
    {
        public bool Vertical;
        public int Line;
        public int Start;
        public int End;
        public bool Outer;

        public Segment(bool vertical, int line, int start, int end, bool outer)
        {
            Vertical = vertical;
            Line = line;
            Start = start;
            End = end;
            Outer = outer;
        }
    }

    // Line/Start/End 是网格线编号 0..size，之后由显示组件转成实际坐标。
    public static Segment[] Build(int size, IReadOnlyList<int> regionIds)
    {
        if (size < 4 || size > 10)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (regionIds == null)
            throw new ArgumentNullException(nameof(regionIds));
        if (regionIds.Count != size * size)
            throw new ArgumentException("区域编号数量必须等于棋盘格子数量。", nameof(regionIds));
        for (int i = 0; i < regionIds.Count; i++)
            if (regionIds[i] < 0 || regionIds[i] >= size)
                throw new ArgumentException("区域编号超出范围。", nameof(regionIds));

        var result = new List<Segment>();
        // 每条内部共边只画一次，避免双重粗边；同区域不画粗边。
        for (int row = 0; row < size; row++)
            for (int column = 1; column < size; column++)
                if (regionIds[row * size + column - 1] != regionIds[row * size + column])
                    result.Add(new Segment(true, column, row, row + 1, false));

        for (int row = 1; row < size; row++)
            for (int column = 0; column < size; column++)
                if (regionIds[(row - 1) * size + column] != regionIds[row * size + column])
                    result.Add(new Segment(false, row, column, column + 1, false));

        // 整盘外框四条线，不逐格重复画。
        result.Add(new Segment(true, 0, 0, size, true));
        result.Add(new Segment(true, size, 0, size, true));
        result.Add(new Segment(false, 0, 0, size, true));
        result.Add(new Segment(false, size, 0, size, true));
        return result.ToArray();
    }
}

