using System;
using System.Collections.Generic;

// 提示快照：一句英文说明（TMP富文本）、依据格、目标格与可应用的叉；提示从不替玩家放动物。
public sealed class HintStep
{
    public string Chinese { get; }   // log only
    public string English { get; }   // the one-line card text (rich text colour words)
    public int[] Evidence { get; }   // lit as the reason (e.g. the purple options)
    public int[] Targets { get; }    // lit as the result (cells to cross, or the cell to place)
    public int[] Crosses { get; }
    public bool IsPlacement { get; }
    public bool CanApply => Crosses.Length > 0;

    public HintStep(string chinese, string english, IEnumerable<int> evidence = null,
        IEnumerable<int> targets = null, IEnumerable<int> crosses = null, bool placement = false)
    {
        Chinese = chinese;
        English = english;
        Evidence = Copy(evidence);
        Targets = Copy(targets);
        Crosses = Copy(crosses);
        IsPlacement = placement;
    }

    private static int[] Copy(IEnumerable<int> values) =>
        values == null ? Array.Empty<int>() : new List<int>(values).ToArray();
}
