using System;
using System.Collections.Generic;

// 只读颜色分区、已确认动物和锁定红叉，不访问 SolutionIndices。
// 普通叉仅是玩家笔记，不作为已证明的排除证据。
// Techniques, simplest first (one short sentence each, like the reference game):
//  1. a placed puppy blocks its row, column, colour and 8 neighbours;
//  2. a colour / row / column has one possible cell left (place it yourself);
//  3. "blocking": a cell that conflicts with EVERY option of some colour/row/column can't hold
//     a puppy. This covers row/column locks ("Purple options all in row 1") as well as
//     shared-neighbour and "a puppy here leaves Teal no space" deductions.
// Deductions the player has already crossed are applied silently so the card always shows
// the next step that changes the board.
public static class LogicalHintService
{
    private enum Group { Colour, Row, Column }

    private static readonly string[] ColourNames =
        { "Pink", "Purple", "Green", "Yellow", "Blue", "Brown", "Teal", "Orange", "Lime", "Gray" };
    private static readonly string[] ColourChinese =
        { "粉", "紫", "绿", "黄", "蓝", "棕", "青绿", "橙", "青柠", "灰蓝" };
    // Darker text versions of the pastel board colours, readable on the cream card.
    private static readonly string[] ColourHex =
        { "#D9536B", "#7E5FCF", "#3E9C48", "#C9940E", "#3570C2", "#9A5B36", "#21948F", "#E07A2C", "#7FA12A", "#6B7B96" };

    public static HintStep Find(LevelData level, BoardState board)
    {
        if (level == null || board == null || board.IsFinished ||
            level.Size != board.Size || level.RegionIds.Count != board.CellCount)
            return None();
        return Find(board.Size, level.RegionIds, board.GetCellState);
    }

    public static HintStep Find(int n, IReadOnlyList<int> regions, Func<int, CellState> stateOf)
    {
        int count = n * n;
        var placed = new List<int>();
        for (int i = 0; i < count; i++)
            if (stateOf(i) == CellState.Placed) placed.Add(i);

        // 1. Leftover crosses around a confirmed puppy — one rule per hint, in teaching order:
        //    its row, then its column, then the ring of 8 neighbours, then the rest of its colour.
        string[] ruleEnglish =
        {
            "Only one puppy per row — cross out the rest of this row",
            "Only one puppy per column — cross out the rest of this column",
            "Puppies can't touch, not even diagonally — cross out the cells around it",
            null, // colour line is built with the colour's name below
        };
        string[] ruleChinese = { "同一行只能有一只，排除这一行其余格子。", "同一列只能有一只，排除这一列其余格子。",
            "小狗不能相邻（含斜角），排除它周围一圈。", "同一颜色只能有一只，排除这个颜色的其余格子。" };
        foreach (int animal in placed)
        {
            int ar = animal / n, ac = animal % n;
            for (int rule = 0; rule < 4; rule++)
            {
                var targets = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (i == animal || stateOf(i) != CellState.Empty) continue;
                    int r = i / n, c = i % n;
                    bool hit = rule == 0 ? r == ar
                        : rule == 1 ? c == ac
                        : rule == 2 ? Math.Abs(r - ar) <= 1 && Math.Abs(c - ac) <= 1
                        : regions[i] == regions[animal];
                    if (hit) targets.Add(i);
                }
                if (targets.Count == 0) continue;
                string english = ruleEnglish[rule] ??
                    $"Only one puppy per color — cross out the other {Colour(regions[animal])} cells";
                return new HintStep($"第{ar + 1}行第{ac + 1}列的动物：{ruleChinese[rule]}", english,
                    new[] { animal }, targets, targets);
            }
        }

        var candidate = new bool[count];
        for (int i = 0; i < count; i++)
        {
            CellState s = stateOf(i);
            bool ok = s != CellState.Placed && s != CellState.Rejected;
            foreach (int animal in placed) if (ok && Conflicts(i, animal, n, regions)) ok = false;
            candidate[i] = ok;
        }
        var doneColour = new bool[n]; var doneRow = new bool[n]; var doneCol = new bool[n];
        foreach (int animal in placed)
        { doneColour[regions[animal]] = true; doneRow[animal / n] = true; doneCol[animal % n] = true; }

        for (int pass = 0; pass <= count; pass++)
        {
            // 2. Only one cell left in a colour, row or column.
            foreach (Group kind in new[] { Group.Colour, Group.Row, Group.Column })
                for (int g = 0; g < n; g++)
                {
                    if (Done(kind, g, doneColour, doneRow, doneCol)) continue;
                    List<int> options = Options(kind, g, n, regions, candidate);
                    if (options.Count == 0) return None();
                    if (options.Count != 1) continue;
                    int cell = options[0];
                    string where = kind == Group.Colour ? $"Only one {Colour(regions[cell])} cell is left"
                        : kind == Group.Row ? $"Row {g + 1} has only one spot left"
                        : $"Column {g + 1} has only one spot left";
                    string wrongCross = stateOf(cell) == CellState.Marked ? " (your cross there is wrong)" : "";
                    return new HintStep(
                        $"{GroupChinese(kind, g)}只剩第{cell / n + 1}行第{cell % n + 1}列，双击放动物。",
                        $"{where} — double-tap it{wrongCross}",
                        Cells(kind, g, n, regions), new[] { cell }, null, true);
                }

            // 3. Blocking: cells that conflict with every option of a colour, row or column.
            bool changed = false;
            foreach (Group kind in new[] { Group.Colour, Group.Row, Group.Column })
                for (int g = 0; g < n; g++)
                {
                    if (Done(kind, g, doneColour, doneRow, doneCol)) continue;
                    List<int> options = Options(kind, g, n, regions, candidate);
                    if (options.Count < 2) continue;
                    var blocked = new List<int>();
                    for (int i = 0; i < count; i++)
                    {
                        if (!candidate[i] || In(kind, g, i, n, regions)) continue;
                        bool all = true;
                        foreach (int o in options) if (!Conflicts(i, o, n, regions)) { all = false; break; }
                        if (all) blocked.Add(i);
                    }
                    if (blocked.Count == 0) continue;
                    var visible = new List<int>();
                    foreach (int i in blocked) if (stateOf(i) == CellState.Empty) visible.Add(i);
                    if (visible.Count > 0) return BlockingStep(kind, g, options, visible, n, regions);
                    // Already crossed by the player: apply silently and keep looking.
                    foreach (int i in blocked) candidate[i] = false;
                    changed = true;
                }
            if (!changed) break;
        }
        return None();
    }

    private static HintStep BlockingStep(Group kind, int g, List<int> options, List<int> targets,
        int n, IReadOnlyList<int> regions)
    {
        // Row/column locks get the reference game's wording.
        if (kind == Group.Colour && SameLine(options, n, true, out int row) && AllInLine(targets, n, true, row))
            return new HintStep($"{ColourChinese[g]}的候选都在第{row + 1}行，这一行其他颜色排除。",
                $"{Colour(g)} options are all in row {row + 1} — exclude other colors", options, targets, targets);
        if (kind == Group.Colour && SameLine(options, n, false, out int col) && AllInLine(targets, n, false, col))
            return new HintStep($"{ColourChinese[g]}的候选都在第{col + 1}列，这一列其他颜色排除。",
                $"{Colour(g)} options are all in column {col + 1} — exclude other colors", options, targets, targets);
        if (kind != Group.Colour && SameColour(options, regions, out int colour) && AllColour(targets, regions, colour))
        {
            string line = kind == Group.Row ? $"Row {g + 1}" : $"Column {g + 1}";
            string lineZh = kind == Group.Row ? $"第{g + 1}行" : $"第{g + 1}列";
            return new HintStep($"{lineZh}的候选都是{ColourChinese[colour]}，其他位置的{ColourChinese[colour]}排除。",
                $"{line}'s options are all {Colour(colour)} — exclude {Colour(colour)} elsewhere", options, targets, targets);
        }
        string subject = kind == Group.Colour ? $"every {Colour(g)} option"
            : kind == Group.Row ? $"every option in row {g + 1}" : $"every option in column {g + 1}";
        string english = targets.Count == 1
            ? $"A puppy here would block {subject} — cross it out"
            : $"A puppy in any of these cells would block {subject} — cross them out";
        return new HintStep($"这些格子会挡住{GroupChinese(kind, g)}的全部候选，排除。", english, options, targets, targets);
    }

    private static HintStep None() => new HintStep("没有找到下一步提示。",
        "No hint found here — try the Reveal booster");

    private static string Colour(int region) =>
        region >= 0 && region < ColourNames.Length ? $"<color={ColourHex[region]}>{ColourNames[region]}</color>" : "this color";

    private static string GroupChinese(Group kind, int g) =>
        kind == Group.Colour ? (g < ColourChinese.Length ? ColourChinese[g] : "该颜色") :
        kind == Group.Row ? $"第{g + 1}行" : $"第{g + 1}列";

    // Two puppies conflict when they share a row, column or colour, or touch (diagonals too).
    private static bool Conflicts(int a, int b, int n, IReadOnlyList<int> regions) =>
        a != b && (a / n == b / n || a % n == b % n || regions[a] == regions[b] ||
            (Math.Abs(a / n - b / n) <= 1 && Math.Abs(a % n - b % n) <= 1));

    private static bool Done(Group kind, int g, bool[] colour, bool[] row, bool[] col) =>
        kind == Group.Colour ? colour[g] : kind == Group.Row ? row[g] : col[g];

    private static bool In(Group kind, int g, int i, int n, IReadOnlyList<int> regions) =>
        (kind == Group.Colour ? regions[i] : kind == Group.Row ? i / n : i % n) == g;

    private static List<int> Options(Group kind, int g, int n, IReadOnlyList<int> regions, bool[] candidate)
    {
        var values = new List<int>();
        for (int i = 0; i < candidate.Length; i++)
            if (candidate[i] && In(kind, g, i, n, regions)) values.Add(i);
        return values;
    }

    private static List<int> Cells(Group kind, int g, int n, IReadOnlyList<int> regions)
    {
        var values = new List<int>();
        for (int i = 0; i < n * n; i++) if (In(kind, g, i, n, regions)) values.Add(i);
        return values;
    }

    private static bool SameLine(List<int> cells, int n, bool row, out int line)
    {
        line = row ? cells[0] / n : cells[0] % n;
        foreach (int i in cells) if ((row ? i / n : i % n) != line) return false;
        return true;
    }

    private static bool AllInLine(List<int> cells, int n, bool row, int line)
    {
        foreach (int i in cells) if ((row ? i / n : i % n) != line) return false;
        return true;
    }

    private static bool SameColour(List<int> cells, IReadOnlyList<int> regions, out int colour)
    {
        colour = regions[cells[0]];
        foreach (int i in cells) if (regions[i] != colour) return false;
        return true;
    }

    private static bool AllColour(List<int> cells, IReadOnlyList<int> regions, int colour)
    {
        foreach (int i in cells) if (regions[i] != colour) return false;
        return true;
    }
}
