using System;
using System.Collections.Generic;

// Explanations based ONLY on already placed, visible pets. No solution lookup.
public sealed class RuleConflictInfo
{
    [Flags] public enum Kind { None = 0, Row = 1, Column = 2, Neighbours = 4, Colour = 8 }
    public Kind Reasons { get; private set; }
    public readonly List<bool[]> Groups = new List<bool[]>(4);
    public bool HasConflicts => Reasons != Kind.None;

    public static RuleConflictInfo Find(LevelData level, BoardState state, int index)
    {
        var result = new RuleConflictInfo();
        if (level == null || state == null || level.Size != state.Size || index < 0 ||
            index >= state.CellCount || level.RegionIds.Count != state.CellCount) return result;
        int n = state.Size, row = index / n, col = index % n, region = level.RegionIds[index];
        bool rowConflict = false, columnConflict = false, colourConflict = false, nearConflict = false;
        var near = new bool[state.CellCount];
        for (int i = 0; i < state.CellCount; i++)
        {
            if (state.GetCellState(i) != CellState.Placed) continue;
            int r = i / n, c = i % n;
            if (r == row) rowConflict = true;
            if (c == col) columnConflict = true;
            if (level.RegionIds[i] == region) colourConflict = true;
            if (Math.Abs(r - row) > 1 || Math.Abs(c - col) > 1) continue;
            nearConflict = true;
            for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr >= 0 && nr < n && nc >= 0 && nc < n) near[nr * n + nc] = true;
            }
        }
        if (rowConflict)
        {
            var mask = new bool[state.CellCount]; for (int c = 0; c < n; c++) mask[row * n + c] = true;
            result.Groups.Add(mask); result.Reasons |= Kind.Row;
        }
        if (columnConflict)
        {
            var mask = new bool[state.CellCount]; for (int r = 0; r < n; r++) mask[r * n + col] = true;
            result.Groups.Add(mask); result.Reasons |= Kind.Column;
        }
        if (nearConflict) { result.Groups.Add(near); result.Reasons |= Kind.Neighbours; }
        if (colourConflict)
        {
            var mask = new bool[state.CellCount];
            for (int i = 0; i < state.CellCount; i++) mask[i] = level.RegionIds[i] == region;
            result.Groups.Add(mask); result.Reasons |= Kind.Colour;
        }
        return result;
    }
}
