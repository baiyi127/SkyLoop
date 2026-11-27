using SkyLoop.Services;

// Test only the production decision to reuse an existing recipe window. Actual
// client flags, button readiness and callback lifecycle need game integration checks.
var fixtures = new (string Name, uint RequestedRecipe, bool Visible, bool ListReady, uint SelectedRecipe, bool Reuse)[]
{
    ("visible ready recipe 34472 is reused instead of reopened", 34472, true, true, 34472, true),
    ("different selected recipe is not reused", 34472, true, true, 34473, false),
    ("hidden window with a matching cached selection is not reused", 34472, false, true, 34472, false),
    ("matching recipe list that is not ready is not reused", 34472, true, false, 34472, false),
    ("zero requested recipe cannot authorize reuse", 0, true, true, 34472, false),
    ("empty selected recipe cannot authorize reuse", 34472, true, true, 0, false),
};

var passed = 0;
foreach (var fixture in fixtures)
{
    var actual = CraftPreparationDecision.CanReuseSelectedRecipe(fixture.RequestedRecipe,
        fixture.Visible, fixture.ListReady, fixture.SelectedRecipe);
    if (actual == fixture.Reuse)
    {
        passed++;
        Console.WriteLine("PASS " + fixture.Name);
    }
    else
        Console.Error.WriteLine($"FAIL {fixture.Name}: expected reuse={fixture.Reuse}; got {actual}");
}
Console.WriteLine($"{passed}/{fixtures.Length} recipe reuse checks passed.");
return passed == fixtures.Length ? 0 : 1;
