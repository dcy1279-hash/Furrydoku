using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public sealed class RestoredChallenge
{
    public SaveData Data { get; }
    public LevelData Level { get; }
    public int CatalogIndex { get; }
    public BoardState Board { get; }
    public LineCompletionTracker Lines { get; }
    public RestoredChallenge(SaveData data, LevelData level, int index,
        BoardState board, LineCompletionTracker lines)
    {
        Data = data; Level = level; CatalogIndex = index; Board = board; Lines = lines;
    }
}

// 存档校验与关卡绑定，不替代文件备份或修改关卡资产。
public static class SaveValidator
{
    public static bool TryRestore(SaveData data, LevelCatalog catalog, LevelData fallback,
        out RestoredChallenge restored, out string error)
    {
        restored = null;
        error = null;
        if (data == null || data.version != 1 || string.IsNullOrWhiteSpace(data.currentLevelId))
            return Fail("存档格式或关卡ID无效。", out error);
        LevelData level = catalog != null ? catalog.FindById(data.currentLevelId) : fallback;
        if (level == null || level.Id != data.currentLevelId ||
            data.levelDataVersion != level.DataVersion || data.boardSize != level.Size ||
            data.levelFingerprint != Fingerprint(level))
            return Fail("存档关卡缺失、版本或棋盘内容已变更；不能把旧状态套到新题。", out error);
        if (!LevelValidator.Validate(level, out error)) return false;
        if (!BoardState.TryRestore(data.boardSize, data.cells, data.remainingLives,
                out BoardState board, out error)) return false;
        var answers = new HashSet<int>(level.SolutionIndices);
        for (int i = 0; i < board.CellCount; i++)
        {
            CellState state = board.GetCellState(i);
            if ((state == CellState.Placed && !answers.Contains(i)) ||
                (state == CellState.Rejected && answers.Contains(i)))
                return Fail("存档动物或红叉位置与关卡答案不符。", out error);
        }
        foreach (int given in level.GivenIndices)
            if (board.GetCellState(given) != CellState.Placed)
                return Fail("存档缺少本关预放的动物。", out error);
        if (!LineCompletionTracker.TryRestore(board, data.reportedRows, data.reportedColumns,
                out LineCompletionTracker lines, out error)) return false;
        string outcome = board.IsWon ? "Won" : board.IsFailed ? "Failed" : "Playing";
        if (data.outcome != outcome || data.runResultRecorded != board.IsFinished ||
            data.mistakes != 3 - board.RemainingLives ||
            double.IsNaN(data.activeSeconds) || double.IsInfinity(data.activeSeconds) ||
            data.activeSeconds < 0d || data.activeSeconds > 1e9)
            return Fail("存档胜负、计时或错误记录不一致。", out error);
        if (!ValidIds(data.completedLevelIds) || !ValidIds(data.unlockedLevelIds))
            return Fail("存档通关/解锁记录无效。", out error);
        var completed = new HashSet<string>(data.completedLevelIds, StringComparer.Ordinal);
        var unlocked = new HashSet<string>(data.unlockedLevelIds, StringComparer.Ordinal);
        if (!unlocked.Contains(level.Id) || (board.IsWon && !completed.Contains(level.Id)))
            return Fail("存档当前关与通关/解锁记录不一致。", out error);
        foreach (string id in completed)
            if (!unlocked.Contains(id)) return Fail("通关记录缺少对应解锁记录。", out error);

        int index = -1;
        if (catalog != null)
            for (int i = 0; i < catalog.Count; i++)
                if (catalog.GetAt(i) == level) { index = i; break; }
        restored = new RestoredChallenge(data, level, index, board, lines);
        return true;
    }

    public static string Fingerprint(LevelData level)
    {
        var text = new StringBuilder();
        text.Append(level.Id).Append('|').Append(level.DataVersion).Append('|')
            .Append(level.Size).Append('|').Append(level.Difficulty).Append('|');
        foreach (int value in level.RegionIds)
            text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        text.Append('|');
        foreach (int value in level.SolutionIndices)
            text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        // Only levels with pre-placed animals extend the text, so older fingerprints stay valid.
        if (level.GivenCount > 0)
        {
            text.Append("|given:");
            foreach (int value in level.GivenIndices)
                text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
        return Hash(text.ToString());
    }

    public static string Hash(string text)
    {
        using (SHA256 algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text)))
                .Replace("-", "").ToLowerInvariant();
    }

    private static bool ValidIds(string[] ids)
    {
        if (ids == null || ids.Length > 10000) return false;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || !unique.Add(id)) return false;
        return true;
    }
    private static bool Fail(string message, out string error) { error = message; return false; }
}
