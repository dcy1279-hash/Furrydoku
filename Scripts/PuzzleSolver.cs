using System;
using System.Collections.Generic;

// 独立按规则搜索，不读取预设答案。最多找两解，防止把多解题当唯一解。
public static class PuzzleSolver
{
    public sealed class Result
    {
        public int Count;
        public bool SearchCompleted;
        public bool BudgetExceeded;
        public int VisitedNodes;
        public int[] FirstSolution;
    }

    public static Result CountSolutions(int size, IReadOnlyList<int> regions,
        int nodeBudget = 2000000)
    {
        if (size < 4 || size > 10 || regions == null || regions.Count != size * size)
            throw new ArgumentException("求解器：棋盘尺寸或区域长度无效。");
        if (nodeBudget < 1)
            throw new ArgumentOutOfRangeException(nameof(nodeBudget));
        for (int i = 0; i < regions.Count; i++)
            if (regions[i] < 0 || regions[i] >= size)
                throw new ArgumentException("求解器：区域编号越界。");

        var search = new Search(size, regions, nodeBudget);
        search.Visit(0);
        search.Result.SearchCompleted =
            !search.Result.BudgetExceeded && search.Result.Count < 2;
        return search.Result;
    }

    private sealed class Search
    {
        public readonly Result Result = new Result();
        private readonly int n;
        private readonly IReadOnlyList<int> regions;
        private readonly int budget;
        private readonly bool[] columns;
        private readonly bool[] usedRegions;
        private readonly int[] answer;

        public Search(int size, IReadOnlyList<int> ids, int nodeBudget)
        {
            n = size; regions = ids; budget = nodeBudget;
            columns = new bool[n]; usedRegions = new bool[n]; answer = new int[n];
        }

        public void Visit(int row)
        {
            if (Result.Count >= 2 || Result.BudgetExceeded)
                return;
            if (Result.VisitedNodes >= budget)
            {
                Result.BudgetExceeded = true;
                return;
            }
            Result.VisitedNodes++;
            if (row == n)
            {
                Result.Count++;
                if (Result.FirstSolution == null)
                    Result.FirstSolution = (int[])answer.Clone();
                return;
            }

            for (int col = 0; col < n; col++)
            {
                int index = row * n + col;
                int region = regions[index];
                if (columns[col] || usedRegions[region])
                    continue;
                // 只有相邻行需要检查斜相邻，不禁止远处的对角线。
                if (row > 0 && Math.Abs(col - answer[row - 1] % n) <= 1)
                    continue;
                columns[col] = true; usedRegions[region] = true; answer[row] = index;
                Visit(row + 1);
                columns[col] = false; usedRegions[region] = false;
                if (Result.Count >= 2 || Result.BudgetExceeded)
                    return;
            }
        }
    }
}
