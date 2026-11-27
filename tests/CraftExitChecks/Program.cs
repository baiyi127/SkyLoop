using SkyLoop.Services;

var idle = new CraftExitSnapshot(true, false, false, false, false);
var fixtures = new (string Name, CraftExitSnapshot Snapshot, TimeSpan Elapsed, CraftExitAction Expected)[]
{
    ("fully idle player is ready for navigation", idle, TimeSpan.Zero, CraftExitAction.Complete),
    ("already idle state completes even after a delayed update", idle, TimeSpan.FromSeconds(16), CraftExitAction.Complete),
    ("missing player cannot authorize navigation", idle with { PlayerAvailable = false }, TimeSpan.Zero, CraftExitAction.FailNoPlayer),
    ("active synthesis waits without closing the recipe book", idle with { CraftActive = true, Crafting = true, Preparing = true, RecipeBookVisible = true }, TimeSpan.FromSeconds(1), CraftExitAction.WaitForActiveCraft),
    ("finished craft still kneeling closes its visible recipe book", idle with { Crafting = true, Preparing = true, RecipeBookVisible = true }, TimeSpan.FromSeconds(1), CraftExitAction.CloseRecipeBook),
    ("idle player with visible recipe book closes it first", idle with { RecipeBookVisible = true }, TimeSpan.FromSeconds(1), CraftExitAction.CloseRecipeBook),
    ("hidden book with crafting flag waits for the player to stand", idle with { Crafting = true }, TimeSpan.FromSeconds(1), CraftExitAction.WaitForIdle),
    ("hidden book with preparing flag waits for the player to stand", idle with { Preparing = true }, TimeSpan.FromSeconds(1), CraftExitAction.WaitForIdle),
    ("hidden book with both crafting flags remains non-actionable", idle with { Crafting = true, Preparing = true }, TimeSpan.FromSeconds(1), CraftExitAction.WaitForIdle),
    ("kneeling state waits immediately before the deadline", idle with { Crafting = true, Preparing = true }, TimeSpan.FromMilliseconds(14999), CraftExitAction.WaitForIdle),
    ("kneeling state fails exactly at the deadline", idle with { Crafting = true, Preparing = true }, TimeSpan.FromSeconds(15), CraftExitAction.FailTimeout),
    ("kneeling state persisting past the deadline fails", idle with { Crafting = true, Preparing = true }, TimeSpan.FromSeconds(16), CraftExitAction.FailTimeout),
    ("recipe book persisting past the deadline fails", idle with { RecipeBookVisible = true }, TimeSpan.FromSeconds(16), CraftExitAction.FailTimeout),
    ("active craft persisting past the exit deadline fails", idle with { CraftActive = true }, TimeSpan.FromSeconds(16), CraftExitAction.FailTimeout),
};

var passed = 0;
foreach (var fixture in fixtures)
{
    var actual = CraftExitDecision.Decide(fixture.Snapshot, fixture.Elapsed);
    if (actual == fixture.Expected)
    {
        passed++;
        Console.WriteLine("PASS " + fixture.Name);
    }
    else
        Console.Error.WriteLine($"FAIL {fixture.Name}: expected {fixture.Expected}; got {actual}");
}

Console.WriteLine($"{passed}/{fixtures.Length} crafting exit checks passed.");
return passed == fixtures.Length ? 0 : 1;
