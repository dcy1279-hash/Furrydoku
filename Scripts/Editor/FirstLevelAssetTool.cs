#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 仅编辑器工具，不挂到场景对象上。
public static class FirstLevelAssetTool
{
    private const string TargetPath =
        "Assets/Data/Levels/Level_001.asset";

    [MenuItem("PetPuzzle/Fill First Level（填入第一关）")]
    public static void FillSelectedFirstLevel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            Show("请停止运行，并等待 Unity 编译和导入完成后再操作。");
            return;
        }

        LevelData target = Selection.activeObject as LevelData;

        if (target == null ||
            AssetDatabase.GetAssetPath(target) != TargetPath ||
            !AssetDatabase.IsMainAsset(target))
        {
            Show("请先在 Project（项目）中选中：\n" + TargetPath);
            return;
        }

        // 只填默认空资源，避免覆盖已经编辑过的关卡。
        if (target.Id != "level_001" ||
            target.DataVersion != 1 ||
            target.Size != 5 ||
            target.Difficulty != 1 ||
            target.RegionIds == null ||
            target.SolutionIndices == null ||
            target.RegionIds.Count != 0 ||
            target.SolutionIndices.Count != 0)
        {
            Show(
                "这个资源已经有数据，或基本信息被修改。\n" +
                "为保护已有内容，本工具不会覆盖它。");
            return;
        }

        LevelData template = FirstLevelFactory.Create();

        try
        {
            // 先验证临时样板，通过后才修改目标资源。
            if (!LevelValidator.Validate(template, out string error))
            {
                Show("第一关样板验证失败，没有修改资源：\n" + error);
                return;
            }

            if (template.Id != "level_001" || template.Size != 5)
            {
                Show("工厂样板不是预期的第一关，没有修改资源。");
                return;
            }

            int[] regions = Copy(template.RegionIds);
            int[] answers = Copy(template.SolutionIndices);

            Undo.RecordObject(target, "Fill First Level（填入第一关）");

            target.InitializeRuntime(
                template.Id,
                template.Size,
                regions,
                answers);

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);

            Debug.Log(
                "第一关已填入并保存：25 个区域编号、5 个答案；" +
                "连通性、规则与唯一解检查通过。",
                target);

            Show(
                "第一关已填入并保存！\n" +
                "Region Ids（区域编号）应为 25；\n" +
                "Solution Indices（答案编号）应为 5。\n\n" +
                "先查看 Inspector，下一步再关联 GameRoot。");
        }
        finally
        {
            // 只销毁临时样板，不删除你的关卡资源。
            Object.DestroyImmediate(template);
        }
    }

    private static int[] Copy(IReadOnlyList<int> source)
    {
        int[] result = new int[source.Count];

        for (int i = 0; i < source.Count; i++)
            result[i] = source[i];

        return result;
    }

    private static void Show(string message)
    {
        EditorUtility.DisplayDialog(
            "First Level（第一关）",
            message,
            "OK（确定）");
    }
}
#endif