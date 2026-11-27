using SkyLoop.Services;

const uint expectedItemId = 31952;
var ready = new TurnInRequestSnapshot(true, true, 1, 1, expectedItemId, 1, true);
var fixtures = new (string Name, uint ExpectedItemId, TurnInRequestSnapshot Snapshot, TurnInRequestMatch Result)[]
{
    ("requested base item 31952 matches", expectedItemId, ready, TurnInRequestMatch.Match),
    ("requested collectable identity 531952 matches base 31952", expectedItemId, ready with { ItemId = 531952 }, TurnInRequestMatch.Match),
    ("normalized HQ identity still requires a collectable request", expectedItemId, ready with { ItemId = 1031952 }, TurnInRequestMatch.Match),
    ("missing expected identity cannot authorize handover", 0, ready, TurnInRequestMatch.Unknown),
    ("empty native and addon request slots wait for data", expectedItemId, ready with { NativeCount = 0, AddonEntryCount = 0, ItemId = 0, RequiredQuantity = 0 }, TurnInRequestMatch.Unknown),
    ("delayed item identity remains pending", expectedItemId, ready with { ItemId = 0 }, TurnInRequestMatch.Unknown),
    ("delayed required quantity remains pending", expectedItemId, ready with { RequiredQuantity = 0 }, TurnInRequestMatch.Unknown),
    ("missing or inactive native agent cannot authorize handover", expectedItemId, ready with { AgentActive = false }, TurnInRequestMatch.Unknown),
    ("native agent tied to another addon cannot authorize handover", expectedItemId, ready with { AddonMatches = false }, TurnInRequestMatch.Unknown),
    ("not ready request window remains pending", expectedItemId, ready with { RequestReady = false }, TurnInRequestMatch.Unknown),
    ("native entries populated before addon entries remain pending", expectedItemId, ready with { AddonEntryCount = 0 }, TurnInRequestMatch.Unknown),
    ("disagreeing nonempty entry counts remain pending", expectedItemId, ready with { AddonEntryCount = 2 }, TurnInRequestMatch.Unknown),
    ("native count outside fixed request capacity cannot authorize handover", expectedItemId, ready with { NativeCount = 6, AddonEntryCount = 6 }, TurnInRequestMatch.Unknown),
    ("negative native count cannot authorize handover", expectedItemId, ready with { NativeCount = -1 }, TurnInRequestMatch.Unknown),
    ("negative addon count cannot authorize handover", expectedItemId, ready with { AddonEntryCount = -1 }, TurnInRequestMatch.Unknown),
    ("coherent two item request is not a Firmament single item request", expectedItemId, ready with { NativeCount = 2, AddonEntryCount = 2 }, TurnInRequestMatch.Mismatch),
    ("populated different base item is rejected", expectedItemId, ready with { ItemId = 31953 }, TurnInRequestMatch.Mismatch),
    ("populated different collectable item is rejected", expectedItemId, ready with { ItemId = 531953 }, TurnInRequestMatch.Mismatch),
    ("quantity two is rejected even for the expected item", expectedItemId, ready with { RequiredQuantity = 2 }, TurnInRequestMatch.Mismatch),
    ("normal item request cannot authorize collectable handover", expectedItemId, ready with { WantCollectible = false }, TurnInRequestMatch.Mismatch),
};

var passed = 0;
foreach (var fixture in fixtures)
{
    var actual = TurnInRequestValidator.Evaluate(fixture.ExpectedItemId, fixture.Snapshot);
    var actionable = TurnInRequestValidator.CanSubmit(fixture.ExpectedItemId, fixture.Snapshot);
    var expectedActionable = fixture.Result == TurnInRequestMatch.Match;
    if (actual == fixture.Result && actionable == expectedActionable)
    {
        passed++;
        Console.WriteLine("PASS " + fixture.Name);
    }
    else
        Console.Error.WriteLine($"FAIL {fixture.Name}: expected {fixture.Result}/CanSubmit={expectedActionable}; got {actual}/CanSubmit={actionable}");
}

Console.WriteLine($"{passed}/{fixtures.Length} turn-in request checks passed.");
return passed == fixtures.Length ? 0 : 1;
