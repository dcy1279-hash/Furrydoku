using System.Collections.Generic;
using UnityEngine;

// 左道具允许读取答案；中间的 LogicalHintService 不复用这里的答案查询。
public static class AnimalRevealService
{
    public static int Choose(LevelData level, BoardState board)
    {
        if (level == null || board == null || board.IsFinished) return -1;
        var choices = new List<int>();
        foreach (int index in level.SolutionIndices)
            if (index >= 0 && index < board.CellCount && !board.IsCellLocked(index))
                choices.Add(index); // 普通叉可被正确动物替换。
        return choices.Count == 0 ? -1 : choices[Random.Range(0, choices.Count)];
    }
}
