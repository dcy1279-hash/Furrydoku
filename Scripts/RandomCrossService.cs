using System.Collections.Generic;
using UnityEngine;

// 右道具仅从真实非答案的空白格抽样，不把玩家猜错的普通叉当作新目标。
public static class RandomCrossService
{
    public const int CrossesPerUse = 3;

    public static int[] Choose(LevelData level, BoardState board)
    {
        if (level == null || board == null || board.IsFinished) return new int[0];
        var answers = new HashSet<int>(level.SolutionIndices);
        var choices = new List<int>();
        for (int i = 0; i < board.CellCount; i++)
            if (board.GetCellState(i) == CellState.Empty && !answers.Contains(i))
                choices.Add(i);
        int count = Mathf.Min(CrossesPerUse, choices.Count);
        var selected = new int[count];
        // 部分 Fisher-Yates 洗牌：不重复抽样，候选不足时全补。
        for (int i = 0; i < count; i++)
        {
            int pick = Random.Range(i, choices.Count);
            int value = choices[pick];
            choices[pick] = choices[i];
            choices[i] = value;
            selected[i] = value;
        }
        return selected;
    }
}
