// 本局结算的只读快照，不挂对象、不联网、不写存档。
// 后续难度策略可订阅 GameController.RunCompleted：按相同棋盘尺寸/难度比较用时，统计连胜。
// 现在只提供数据，不启用自动升降难度，也不在游玩中改答案/颜色。
public sealed class LevelRunResult
{
    public string LevelId { get; }
    public int DataVersion { get; }
    public int BoardSize { get; }
    public int Difficulty { get; }
    public bool Won { get; }
    public double ActiveSeconds { get; }
    public int Mistakes { get; }

    public LevelRunResult(LevelData level, bool won, double activeSeconds, int mistakes)
    {
        LevelId = level.Id;
        DataVersion = level.DataVersion;
        BoardSize = level.Size;
        Difficulty = level.Difficulty;
        Won = won;
        ActiveSeconds = activeSeconds;
        Mistakes = mistakes;
    }
}
