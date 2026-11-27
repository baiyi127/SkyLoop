using System.Text.Json;
using SkyLoop;
using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("existing recipe 34472 config with hidden item 34472 watches result 31952", ExistingConfigIgnoresHiddenItem),
    ("switching item-increase to craft-finished ignores the stale custom item", SwitchCompletionKind),
    ("craft-finished prefers the prepared recipe result over another active recipe", PreparedRecipeWins),
    ("manual craft-finished uses the active recipe result", ManualCraftUsesActiveRecipe),
    ("craft-finished without a recipe cannot use a stale custom item", MissingRecipeDoesNotFallBackToCustomItem),
    ("item-increase preserves the explicit custom item", ExplicitItemIncreaseTarget),
    ("item-increase without a custom item cannot use a recipe result", MissingExplicitItem),
    ("macro-finished does not watch an inventory item", MacroFinishedDoesNotWatchItems),
    ("craft-finished always requires one output regardless of a hidden minimum", CraftFinishedIgnoresHiddenMinimum),
    ("item-increase preserves its configured positive minimum", ExplicitMinimumPreserved),
    ("macro-finished has no inventory increase requirement", MacroFinishedRequiresNoIncrease),
    ("craft completed before the final macro retains the pre-craft baseline", SplitMacroUsesPreparedBaseline),
    ("explicit item-increase starts its baseline at that macro step", ItemIncreaseUsesCurrentBaseline),
    ("a new craft cycle uses its new baseline rather than the previous output", NewCycleUsesFreshBaseline),
};

var passed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} macro result checks passed.");
return passed == checks.Length ? 0 : 1;

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectTarget(MacroResultTarget actual, uint itemId, bool preparedBaseline)
{
    Expect(actual.ItemId == itemId, $"expected watched item {itemId}; got {actual.ItemId}");
    Expect(actual.UsePreparedBaseline == preparedBaseline, $"expected UsePreparedBaseline={preparedBaseline}; got {actual.UsePreparedBaseline}");
}

static void ExistingConfigIgnoresHiddenItem()
{
    // Recipe IDs and item IDs are different namespaces. This persisted field caused
    // a successful recipe 34472 craft to check unrelated item 34472 instead of 31952.
    const string oldConfig = """
        {"Version":1,"RecipeId":34472,"Repeat":true,"Macros":[
          {"Number":64,"Shared":true,"Completion":0,"ItemId":0},
          {"Number":0,"Shared":true,"Completion":1,"ItemId":34472,"MinimumIncrease":99}
        ]}
        """;
    var config = JsonSerializer.Deserialize<Configuration>(oldConfig,
        new JsonSerializerOptions { IncludeFields = true })!;
    Expect(config.RecipeId == 34472 && config.Macros.Count == 2, "fixture must reproduce the persisted two-step recipe configuration");
    var final = config.Macros[^1];
    Expect(final.ItemId == 34472, "fixture must retain the stale hidden item field");
    var target = MacroResultTarget.Resolve(final.Completion, final.ItemId, 31952, 0);
    ExpectTarget(target, 31952, true);
    var requiredIncrease = MacroResultTarget.RequiredIncrease(final.Completion, final.MinimumIncrease);
    var baseline = target.GetBaseline(10, 11);
    Expect(requiredIncrease == 1 && baseline == 10 && 11 - baseline >= requiredIncrease,
        "one actual product must satisfy craft-finished despite both hidden fields being stale");
    Expect(final.ItemId == 34472 && final.MinimumIncrease == 99,
        "runtime policy must protect existing configurations without depending on a UI edit or migration");
}

static void SwitchCompletionKind()
{
    var step = new MacroStep { Completion = CompletionKind.ItemIncreased, ItemId = 34472 };
    ExpectTarget(MacroResultTarget.Resolve(step.Completion, step.ItemId, 31952, 31952), 34472, false);
    step.Completion = CompletionKind.CraftFinished; // Simulate a saved config whose hidden field was not cleared.
    ExpectTarget(MacroResultTarget.Resolve(step.Completion, step.ItemId, 31952, 31952), 31952, true);
}

static void PreparedRecipeWins() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.CraftFinished, 34472, 31952, 31953), 31952, true);

static void ManualCraftUsesActiveRecipe() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.CraftFinished, 34472, 0, 31952), 31952, false);

static void MissingRecipeDoesNotFallBackToCustomItem() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.CraftFinished, 34472, 0, 0), 0, false);

static void ExplicitItemIncreaseTarget() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.ItemIncreased, 34472, 31952, 31952), 34472, false);

static void MissingExplicitItem() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.ItemIncreased, 0, 31952, 31952), 0, false);

static void MacroFinishedDoesNotWatchItems() =>
    ExpectTarget(MacroResultTarget.Resolve(CompletionKind.MacroFinished, 34472, 31952, 31952), 0, false);

static void CraftFinishedIgnoresHiddenMinimum()
{
    foreach (var minimum in new[] { -5, 0, 1, 99 })
        Expect(MacroResultTarget.RequiredIncrease(CompletionKind.CraftFinished, minimum) == 1,
            $"craft-finished must require exactly one product rather than the hidden minimum {minimum}");
}

static void ExplicitMinimumPreserved()
{
    Expect(MacroResultTarget.RequiredIncrease(CompletionKind.ItemIncreased, 99) == 99,
        "explicit item-increase must preserve a user's requested quantity");
    foreach (var minimum in new[] { -1, 0, 1 })
        Expect(MacroResultTarget.RequiredIncrease(CompletionKind.ItemIncreased, minimum) == 1,
            "per-step item checks need a positive lower bound");
}

static void MacroFinishedRequiresNoIncrease() =>
    Expect(MacroResultTarget.RequiredIncrease(CompletionKind.MacroFinished, 99) == 0,
        "macro interpreter completion must not depend on an invisible inventory quantity setting");

static void SplitMacroUsesPreparedBaseline()
{
    const int preCraftCount = 10;
    const int countWhenFinalMacroStarts = 11;
    var target = MacroResultTarget.Resolve(CompletionKind.CraftFinished, 34472, 31952, 0);
    var baseline = target.GetBaseline(preCraftCount, countWhenFinalMacroStarts);
    Expect(baseline == 10, "macro 0 must use the baseline from before synthesis, even if macro 64 already completed it");
    Expect(countWhenFinalMacroStarts - baseline == 1, "the already-arrived product must count as this craft's result");
}

static void ItemIncreaseUsesCurrentBaseline()
{
    var target = MacroResultTarget.Resolve(CompletionKind.ItemIncreased, 31952, 31952, 31952);
    Expect(target.GetBaseline(5, 6) == 6, "explicit per-step quantity checks must not reuse an earlier recipe baseline");
}

static void NewCycleUsesFreshBaseline()
{
    var target = MacroResultTarget.Resolve(CompletionKind.CraftFinished, 34472, 31952, 0);
    Expect(target.GetBaseline(5, 6) == 5, "first cycle baseline");
    var secondCycleBaseline = target.GetBaseline(6, 6);
    Expect(secondCycleBaseline == 6 && 6 - secondCycleBaseline == 0,
        "the prior cycle's product must not satisfy the new cycle's success condition");
}
