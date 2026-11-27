using System.Numerics;
using SkyLoop;
using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("empty configuration needs no manually recorded NPC locations", () =>
    {
        var config = new Configuration();
        FirmamentLocations.Apply(config, []);
        ExpectLocations(config, 1031690, 1031691);
    }),
    ("manual locations are replaced with the verified fixed spots", () =>
    {
        var config = new Configuration
        {
            TurnInNpc = new() { Saved = true, DataId = 999, TerritoryId = 1, Name = "old manual", X = 999,
                ApproachSaved = true, ApproachX = 999, ApproachY = 999, ApproachZ = 999 },
            ExchangeNpc = new() { Saved = true, DataId = 998, TerritoryId = 2, Name = "other NPC", X = -999 },
        };
        FirmamentLocations.Apply(config, []);
        ExpectLocations(config, 1031690, 1031691);
    }),
    ("visible early phase NPCs override saved later phase IDs", () =>
    {
        var config = new Configuration();
        FirmamentLocations.Apply(config, []);
        FirmamentLocations.Apply(config, [1031677, 1031680]);
        ExpectLocations(config, 1031677, 1031680);
    }),
    ("visible later phase NPCs override saved early phase IDs", () =>
    {
        var config = WithSavedEarlyIds();
        FirmamentLocations.Apply(config, [1031690, 1031691]);
        ExpectLocations(config, 1031690, 1031691);
    }),
    ("each NPC resolves independently from unrelated visible objects", () =>
    {
        var config = new Configuration();
        FirmamentLocations.Apply(config, [1, 999999, 1031677, 1031691]);
        ExpectLocations(config, 1031677, 1031691);
    }),
    ("unloaded NPCs retain valid saved phase IDs", () =>
    {
        var config = WithSavedEarlyIds();
        FirmamentLocations.Apply(config, [1, 2, 3]);
        ExpectLocations(config, 1031677, 1031680);
        FirmamentLocations.Apply(config, []);
        ExpectLocations(config, 1031677, 1031680);
    }),
    ("unsaved IDs do not override the later-phase fallback", () =>
    {
        var config = WithSavedEarlyIds();
        config.TurnInNpc.Saved = config.ExchangeNpc.Saved = false;
        FirmamentLocations.Apply(config, []);
        ExpectLocations(config, 1031690, 1031691);
    }),
    ("both visible phases choose the later placement regardless of order", () =>
    {
        foreach (var ids in new[]
        {
            new uint[] { 1031677, 1031680, 1031690, 1031691 },
            new uint[] { 1031691, 1031690, 1031680, 1031677, 1031690 },
        })
        {
            var config = WithSavedEarlyIds();
            FirmamentLocations.Apply(config, ids);
            ExpectLocations(config, 1031690, 1031691);
        }
    }),
    ("craft location macros and thresholds remain unchanged", () =>
    {
        var point = new NpcLocation { Saved = true, Name = "my crafting spot", TerritoryId = 777,
            X = 1.25f, Y = 2.5f, Z = 3.75f, ApproachSaved = true, ApproachX = 4, ApproachY = 5, ApproachZ = 6 };
        var steps = new List<MacroStep> { new() { Number = 64, Shared = false, TimeoutSeconds = 79 } };
        var config = new Configuration { CraftLocation = point, Macros = steps,
            FreeSlotThreshold = 12, ScripThreshold = 40, ScripReserve = 5, Repeat = false };
        FirmamentLocations.Apply(config, [1031677, 1031680]);
        Expect(ReferenceEquals(config.CraftLocation, point) && point.Name == "my crafting spot"
            && point.TerritoryId == 777 && point.Position == new Vector3(1.25f, 2.5f, 3.75f)
            && point.ApproachPosition == new Vector3(4, 5, 6), "must preserve the user's entire crafting location");
        Expect(ReferenceEquals(config.Macros, steps) && steps.Single().Number == 64
            && !steps[0].Shared && steps[0].TimeoutSeconds == 79, "must preserve macro steps");
        Expect(config.FreeSlotThreshold == 12 && config.ScripThreshold == 40 && config.ScripReserve == 5
            && !config.Repeat, "must preserve loop settings");
    }),
    ("legacy object aliases cannot overwrite the crafting point", () =>
    {
        var point = new NpcLocation { Saved = true, Name = "home", DataId = 1031677, X = 11, Y = 22, Z = 33 };
        var config = new Configuration { CraftLocation = point, TurnInNpc = point, ExchangeNpc = point };
        FirmamentLocations.Apply(config, []);
        Expect(point.Position == new Vector3(11, 22, 33) && point.Name == "home", "replacing NPC defaults must not mutate an aliased crafting location");
        ExpectLocations(config, 1031677, 1031691);
    }),
};

var passed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} Firmament location checks passed.");
return passed == checks.Length ? 0 : 1;

static Configuration WithSavedEarlyIds() => new()
{
    TurnInNpc = new() { Saved = true, DataId = 1031677 },
    ExchangeNpc = new() { Saved = true, DataId = 1031680 },
};

static void ExpectLocations(Configuration config, uint turnInId, uint exchangeId)
{
    ExpectLocation(config.TurnInNpc, "波特金", turnInId,
        turnInId == 1031677 ? new(52.7582f, -16f, 168.937f) : new(50.5531f, -16.0067f, 168.449f),
        new(47.023697f, -16f, 167.64795f));
    ExpectLocation(config.ExchangeNpc, "艾妮", exchangeId,
        exchangeId == 1031680 ? new(40.5088f, -16f, 138.504f) : new(37.7104f, -16f, 140.136f),
        new(39.386646f, -16f, 141.85832f));
}

static void ExpectLocation(NpcLocation point, string name, uint id, Vector3 anchor, Vector3 approach)
{
    Expect(point.Saved && point.TerritoryId == 886 && point.DataId == id && point.Name == name,
        $"expected Firmament NPC {name} ({id}) to be immediately configured");
    Expect(Vector3.Distance(point.Position, anchor) < 0.0001f, "NPC anchor must match the chosen phase");
    Expect(point.ApproachSaved && Vector3.Distance(point.ApproachPosition, approach) < 0.0001f,
        "interaction point must use the user's verified fixed world coordinates");
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
