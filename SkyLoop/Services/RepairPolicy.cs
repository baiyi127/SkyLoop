using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyLoop.Services;

public static class RepairPolicy
{
    public const int TriggerCondition = 3000;
    public static bool ShouldRepair(int condition) => condition >= 0 && condition < TriggerCondition;

    public static bool TryAllocateDarkMatter(IReadOnlyList<int> requiredGrades,
        IReadOnlyDictionary<int, int> available, out Dictionary<int, int> allocation)
    {
        allocation = new Dictionary<int, int>();
        var remaining = available.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var requirement in requiredGrades.OrderByDescending(grade => grade))
        {
            if (requirement <= 0) { allocation.Clear(); return false; }
            var grade = remaining.Keys.Where(grade => grade >= requirement && remaining[grade] > 0)
                .OrderBy(grade => grade).FirstOrDefault();
            if (grade == 0) { allocation.Clear(); return false; }
            remaining[grade]--;
            allocation[grade] = allocation.GetValueOrDefault(grade) + 1;
        }
        return true;
    }

    public static bool IsSelfRepairConfirmation(string text)
    {
        var normalized = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        return normalized.StartsWith("要修理当前所有装备吗？", StringComparison.Ordinal)
            && !new[] { "费用", "金币", "兵装库", "接连", "分页" }.Any(normalized.Contains);
    }
}
