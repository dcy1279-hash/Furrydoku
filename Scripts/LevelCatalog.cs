using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelCatalog", menuName = "PetPuzzle/Level Catalog（关卡目录）")]
public class LevelCatalog : ScriptableObject
{
    [SerializeField] private LevelData[] levels = new LevelData[0];
    public int Count => levels == null ? 0 : levels.Length;
    public LevelData GetAt(int index) => index >= 0 && index < Count ? levels[index] : null;

    public LevelData FindById(string id)
    {
        for (int i = 0; i < Count; i++)
            if (levels[i] != null && string.Equals(levels[i].Id, id, StringComparison.Ordinal))
                return levels[i];
        return null;
    }

    public LevelData GetNext(int currentIndex) => GetAt(currentIndex + 1);

    // 启动检查目录完整性；每道题的连通/规则/唯一解由加载器在进入该题前检查。
    // 不在启动时对未来成千上万道题同步求解。
    public bool ValidateIndex(out string error)
    {
        error = null;
        if (Count == 0)
        {
            error = "关卡目录为空，请加入已制作的 LevelData（关卡数据）资源。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Count; i++)
        {
            LevelData item = levels[i];
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
            {
                error = $"关卡目录第 {i + 1} 项为空、ID 为空或 ID 重复。";
                return false;
            }
        }
        return true;
    }
}
