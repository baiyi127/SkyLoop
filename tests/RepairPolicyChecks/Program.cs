using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("unknown condition never triggers repair", () => Require(!RepairPolicy.ShouldRepair(-1))),
    ("broken equipment triggers repair", () => Require(RepairPolicy.ShouldRepair(0))),
    ("strict threshold includes 2999", () => Require(RepairPolicy.ShouldRepair(2999))),
    ("exactly ten percent does not trigger", () => Require(!RepairPolicy.ShouldRepair(3000))),
    ("full and absent equipment do not trigger", () => Require(!RepairPolicy.ShouldRepair(30000) && !RepairPolicy.ShouldRepair(int.MaxValue))),
    ("higher grade substitutes for lower grade", HigherGradeSubstitutes),
    ("scarce highest grade stays reserved for highest requirement", ReserveHighestGrade),
    ("lowest sufficient grade is consumed first", PreferLowestGrade),
    ("total dark matter shortage rejects whole allocation", RejectTotalShortage),
    ("inadequate grades reject despite sufficient total quantity", RejectGradeShortage),
    ("invalid resource requirement rejects without partial allocation", RejectInvalidGrade),
    ("empty repair set needs no resources", EmptyRepairSet),
    ("allocation leaves supplied inventory and requirements unchanged", DoesNotMutateInputs),
    ("current equipped self repair confirmation is accepted", () => Require(RepairPolicy.IsSelfRepairConfirmation("要修理当前所有装备吗？\r\n消耗：8级暗物质 × 2"))),
    ("whitespace formatting does not alter confirmation identity", () => Require(RepairPolicy.IsSelfRepairConfirmation("要修理当前所有装备吗？\r\n\t"))),
    ("all pages confirmation 17221 is rejected", () => Require(!RepairPolicy.IsSelfRepairConfirmation("确定要接连将装备中、兵装库、物品分页内\r\n所有可以修理的装备都修好吗？"))),
    ("mender gold confirmation 861 is rejected", () => Require(!RepairPolicy.IsSelfRepairConfirmation("要修理当前所有装备吗？\r\n费用：100金币"))),
    ("individual item confirmation 862 is rejected", () => Require(!RepairPolicy.IsSelfRepairConfirmation("要修理下列装备吗？\r\n消耗：8级暗物质"))),
    ("unrelated and empty confirmations are rejected", () => Require(!RepairPolicy.IsSelfRepairConfirmation("确定兑换物品吗？") && !RepairPolicy.IsSelfRepairConfirmation(""))),
};
var passed = 0;
foreach (var check in checks)
{
    try { check.Run(); passed++; Console.WriteLine("PASS " + check.Name); }
    catch (Exception exception) { Console.Error.WriteLine("FAIL " + check.Name + ": " + exception.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} repair policy checks passed.");
return passed == checks.Length ? 0 : 1;

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("Expected condition was false.");
}

static void HigherGradeSubstitutes()
{
    Require(RepairPolicy.TryAllocateDarkMatter([1, 4, 7], new Dictionary<int, int> { [8] = 3 }, out var result));
    Require(result.Count == 1 && result[8] == 3);
}

static void ReserveHighestGrade()
{
    Require(RepairPolicy.TryAllocateDarkMatter([1, 8], new Dictionary<int, int> { [7] = 1, [8] = 1 }, out var result));
    Require(result[7] == 1 && result[8] == 1);
}

static void PreferLowestGrade()
{
    Require(RepairPolicy.TryAllocateDarkMatter([1], new Dictionary<int, int> { [1] = 1, [8] = 5 }, out var result));
    Require(result.Count == 1 && result[1] == 1);
}

static void RejectTotalShortage()
{
    Require(!RepairPolicy.TryAllocateDarkMatter([1, 2], new Dictionary<int, int> { [8] = 1 }, out var result));
    Require(result.Count == 0);
}

static void RejectGradeShortage()
{
    Require(!RepairPolicy.TryAllocateDarkMatter([8, 8], new Dictionary<int, int> { [7] = 10, [8] = 1 }, out var result));
    Require(result.Count == 0);
}

static void RejectInvalidGrade()
{
    Require(!RepairPolicy.TryAllocateDarkMatter([8, 0], new Dictionary<int, int> { [8] = 2 }, out var result));
    Require(result.Count == 0);
}

static void EmptyRepairSet()
{
    Require(RepairPolicy.TryAllocateDarkMatter([], new Dictionary<int, int>(), out var result));
    Require(result.Count == 0);
}

static void DoesNotMutateInputs()
{
    var requirements = new[] { 1, 8 };
    var inventory = new Dictionary<int, int> { [1] = 2, [8] = 1 };
    Require(RepairPolicy.TryAllocateDarkMatter(requirements, inventory, out _));
    Require(requirements.SequenceEqual(new[] { 1, 8 }) && inventory[1] == 2 && inventory[8] == 1);
}
