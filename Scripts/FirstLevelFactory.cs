using UnityEngine;

// 第一关固定测试数据，不是批量随机生成器。
public static class FirstLevelFactory
{
    public static LevelData Create()
    {
        LevelData level = ScriptableObject.CreateInstance<LevelData>();
        level.name = "Level_001_Runtime";
        level.InitializeRuntime("level_001", 5,
            new int[]
            {
                0, 0, 1, 1, 1,
                0, 2, 1, 1, 1,
                2, 2, 1, 1, 1,
                2, 2, 3, 3, 4,
                2, 2, 4, 4, 4
            },
            new int[] { 1, 8, 10, 17, 24 });
        return level;
    }

    // 仅用于测试外观；区域编号才是玩法依据，不比较颜色判断规则。
    public static Color GetRegionColor(int regionId)
    {
        // "F2 +1" palette chosen by the user: Morandi softness with macaron clarity, a little less
        // grey than plain Morandi and one lightness step brighter than F2.
        Color[] colors =
        {
            new Color(0.902f, 0.561f, 0.616f),  // 0 Pink
            new Color(0.690f, 0.580f, 0.882f),  // 1 Purple
            new Color(0.510f, 0.827f, 0.537f),  // 2 Green
            new Color(0.937f, 0.808f, 0.482f),  // 3 Yellow
            new Color(0.478f, 0.659f, 0.863f),  // 4 Blue
            new Color(0.776f, 0.529f, 0.365f),  // 5 Brown
            new Color(0.404f, 0.816f, 0.788f),  // 6 Teal
            new Color(0.945f, 0.686f, 0.514f),  // 7 Orange
            new Color(0.729f, 0.847f, 0.455f),  // 8 Lime
            new Color(0.576f, 0.639f, 0.765f)   // 9 Gray
        };
        return colors[regionId];
    }
}
