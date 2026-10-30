using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Level_001", menuName = "PetPuzzle/Level Data")]
public class LevelData : ScriptableObject
{
    [Header("Basic Info（基本信息）")]
    [SerializeField] private string id = "level_001";
    [SerializeField, Min(1)] private int dataVersion = 1;
    [SerializeField, Range(4, 10)] private int size = 5;
    [SerializeField, Min(1)] private int difficulty = 1;
    [Header("Regions（颜色区域编号）")]
    [SerializeField] private int[] regionIds = new int[0];
    [Header("Solution（动物答案格子编号）")]
    [SerializeField] private int[] solutionIndices = new int[0];
    [Header("Given（开局预放的动物，必须是答案格）")]
    [SerializeField] private int[] givenIndices = new int[0];

    public string Id => id;
    public int DataVersion => dataVersion;
    public int Size => size;
    public int Difficulty => difficulty;
    public IReadOnlyList<int> RegionIds => regionIds;
    public IReadOnlyList<int> SolutionIndices => solutionIndices;
    public IReadOnlyList<int> GivenIndices => givenIndices ?? System.Array.Empty<int>();
    public int GivenCount => givenIndices == null ? 0 : givenIndices.Length;

    // 只初始化运行时创建的测试数据，不写入或覆盖工程中的 .asset。
    public void InitializeRuntime(string levelId, int boardSize, int[] regions, int[] solution, int[] given = null)
    {
        if (regions == null || solution == null)
            throw new ArgumentNullException("关卡区域和答案不能为 null。");
        id = levelId;
        dataVersion = 1;
        size = boardSize;
        difficulty = 1;
        regionIds = (int[])regions.Clone();
        solutionIndices = (int[])solution.Clone();
        givenIndices = given != null ? (int[])given.Clone() : new int[0];
    }
}
