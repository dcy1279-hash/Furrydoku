using System;

// JSON 数据，不挂对象、不保存 GameObject/Sprite 等运行时引用。
[Serializable]
public sealed class SaveData
{
    public int version;
    public string currentLevelId;
    public int levelDataVersion;
    public string levelFingerprint;
    public int boardSize;
    public int[] cells;
    public int remainingLives;
    public bool[] reportedRows;
    public bool[] reportedColumns;
    public string[] completedLevelIds;
    public string[] unlockedLevelIds;
    public double activeSeconds;
    public int mistakes;
    public string outcome; // Playing / Won / Failed，由稳定棋盘推导并复核。
    public bool runResultRecorded;
}

[Serializable]
public sealed class SaveEnvelope
{
    public int version;
    public string payload;
    public string sha256; // 用于发现意外损坏，不是加密或防作弊。
}
