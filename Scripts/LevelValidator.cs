using System;
using System.Collections.Generic;

public static class LevelValidator
{
    public static bool Validate(LevelData level, out string error, int nodeBudget = 2000000)
    {
        error = null;
        if (level == null || string.IsNullOrWhiteSpace(level.Id) || level.DataVersion != 1)
            return Fail("关卡缺失、ID 为空或数据版本不支持。", out error);
        int n = level.Size;
        if (n < 4 || n > 10 || level.RegionIds == null || level.RegionIds.Count != n * n)
            return Fail("棋盘尺寸必须为 4～10，区域长度必须为 N×N。", out error);
        if (level.Difficulty < 1)
            return Fail("难度标签必须为正数。", out error);

        var counts = new int[n];
        for (int i = 0; i < n * n; i++)
        {
            int id = level.RegionIds[i];
            if (id < 0 || id >= n)
                return Fail("区域编号必须为 0 到 N-1。", out error);
            counts[id]++;
        }
        for (int id = 0; id < n; id++)
        {
            if (counts[id] == 0)
                return Fail("每个区域都必须至少有一个格子。", out error);
            if (!IsConnected(n, level.RegionIds, id, counts[id]))
                return Fail($"区域 {id} 不共边连通。", out error);
        }
        if (!ValidateSolution(n, level.RegionIds, level.SolutionIndices, out error))
            return false;
        var answers = new HashSet<int>(level.SolutionIndices);
        var givens = new HashSet<int>();
        foreach (int index in level.GivenIndices)
            if (!answers.Contains(index) || !givens.Add(index))
                return Fail("预放动物必须是不重复的答案格。", out error);
        if (givens.Count >= n)
            return Fail("预放动物不能占满全部答案。", out error);

        PuzzleSolver.Result solved = PuzzleSolver.CountSolutions(n, level.RegionIds, nodeBudget);
        if (solved.BudgetExceeded || !solved.SearchCompleted && solved.Count < 2)
            return Fail("搜索预算用完，唯一性未知，不能用于扣命判定。", out error);
        if (solved.Count != 1)
            return Fail($"关卡不是唯一解（搜索到 {solved.Count} 解，2 表示至少两解）。", out error);

        var expected = new HashSet<int>(level.SolutionIndices);
        foreach (int index in solved.FirstSolution)
            if (!expected.Contains(index))
                return Fail("预设答案与唯一解不一致。", out error);
        return true;
    }

    // 同时用于关卡答案检查和通关前的完整规则复验。
    public static bool ValidateSolution(int n, IReadOnlyList<int> regions,
        IReadOnlyList<int> answer, out string error)
    {
        error = null;
        if (n < 4 || n > 10 || regions == null || regions.Count != n * n ||
            answer == null || answer.Count != n)
            return Fail("必须正好有 N 个答案，且区域数据完整。", out error);
        var rows = new bool[n]; var cols = new bool[n]; var ids = new bool[n];
        for (int i = 0; i < answer.Count; i++)
        {
            int index = answer[i];
            if (index < 0 || index >= n * n)
                return Fail("答案格子编号越界。", out error);
            int row = index / n, col = index % n, id = regions[index];
            if (id < 0 || id >= n || rows[row] || cols[col] || ids[id])
                return Fail("答案存在同一行、同一列或同一区域重复。", out error);
            rows[row] = true; cols[col] = true; ids[id] = true;
            for (int j = 0; j < i; j++)
                if (Math.Abs(row - answer[j] / n) <= 1 && Math.Abs(col - answer[j] % n) <= 1)
                    return Fail("两个动物在周围八格范围内相邻。", out error);
        }
        return true;
    }

    private static bool IsConnected(int n, IReadOnlyList<int> regions, int id, int expected)
    {
        int start = -1;
        for (int i = 0; i < regions.Count; i++)
            if (regions[i] == id) { start = i; break; }
        var visited = new bool[n * n]; var queue = new Queue<int>();
        queue.Enqueue(start); visited[start] = true; int count = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(); count++;
            int row = i / n, col = i % n;
            if (row > 0) Enqueue(i - n, id, regions, visited, queue);
            if (row < n - 1) Enqueue(i + n, id, regions, visited, queue);
            if (col > 0) Enqueue(i - 1, id, regions, visited, queue);
            if (col < n - 1) Enqueue(i + 1, id, regions, visited, queue);
        }
        return count == expected;
    }

    private static void Enqueue(int i, int id, IReadOnlyList<int> regions,
        bool[] visited, Queue<int> queue)
    {
        if (!visited[i] && regions[i] == id)
        { visited[i] = true; queue.Enqueue(i); }
    }

    private static bool Fail(string message, out string error)
    { error = message; return false; }
}
