using System.Reflection;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using SkyLoop;
using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("start rejects an empty macro queue", RejectEmptyQueue),
    ("start rejects invalid macro numbers", RejectInvalidMacro),
    ("visible macro addresses preserve the game grid number in both banks", NativeMacroAddressing),
    ("start accepts macro zero and 99 in both banks", AcceptMacroBoundaries),
    ("macro 64 keeps its number through configuration reload and start", PreserveSavedMacro64),
    ("a new macro defaults to visible slot zero in the shared bank", DefaultSharedBank),
    ("start requires navigation for automatic errands", RejectMissingNavigation),
    ("start rejects locations from a different territory", RejectDifferentTerritory),
    ("automatic crafting requires a final result condition", RejectPrematureCraftCompletion),
    ("inventory threshold includes exactly ten free slots", SlotThreshold),
    ("recipe 34472 repeats with twelve slots when the threshold is ten", RepeatWithTwelveSlots),
    ("recipe 34472 submits with twelve slots when the threshold is twelve", SubmitAtTwelveSlots),
    ("currency threshold includes exactly the configured amount", CurrencyThreshold),
    ("currency exchange takes priority over submission", ExchangePriority),
    ("macro queue waits and advances strictly in configured order", MacroOrdering),
    ("failed macro prevents the following macro", MacroFailure),
    ("exchange and submission return home before crafting", ErrandRoundTrip),
    ("submission without reclaimed space stops the loop", NoTurnInProgress),
    ("insufficient currency headroom exchanges before the threshold", ExchangeForHeadroom),
    ("headroom exchange must actually spend currency", RejectNoProgressHeadroomExchange),
    ("combat interrupts navigation and clears owned work", CombatStops),
    ("territory changes stop the loop", TerritoryStops),
    ("explicit stop clears navigation macro and interaction work", StopClearsWork),
    ("one-pass mode stops after one completed queue", OnePassStops),
};
var passed = 0;
foreach (var check in checks)
{
    Reset();
    try { check.Run(); Console.WriteLine("PASS " + check.Name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} flow checks passed.");
return passed == checks.Length ? 0 : 1;

static Configuration Config()
{
    var config = new Configuration
    {
        Macros = [new() { Number = 1, Completion = CompletionKind.CraftFinished, ItemId = 100 }],
        CraftLocation = new() { Saved = true, TerritoryId = 886, Name = "home" },
        TurnInNpc = new() { Saved = true, TerritoryId = 886, DataId = 101, Name = "turnin" },
        ExchangeNpc = new() { Saved = true, TerritoryId = 886, DataId = 102, Name = "exchange" },
        RewardItemId = 123,
    };
    return config;
}

static void Reset()
{
    Plugin.ObjectTable.LocalPlayer = new();
    Plugin.ClientState.TerritoryType = 886;
    Plugin.Condition.Clear();
    InventoryService.Slots = 20;
    InventoryService.Scrips = 0;
    MacroService.NextResult = WorkResult.Pending;
    MacroService.PrepareResult = WorkResult.Complete;
    MacroService.Started.Clear();
    MacroService.Resets = 0;
    NavigationService.Available = true;
    NavigationService.NextResult = WorkResult.Pending;
    NavigationService.Destinations.Clear();
    NavigationService.Stops = 0;
    FirmamentService.TurnInResult = WorkResult.Pending;
    FirmamentService.ExchangeResult = WorkResult.Pending;
    FirmamentService.ExchangeRequired = false;
    FirmamentService.Resets = 0;
}

static void Tick(LoopController loop)
{
    // Skip the production's 100ms poll throttle; run its real transition logic unchanged.
    typeof(LoopController).GetField("nextTick", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(loop, 0L);
    loop.Update();
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectState(LoopController loop, LoopState state) =>
    Expect(loop.State == state, $"expected {state}; got {loop.State}: {loop.Status}");

static void RejectEmptyQueue()
{
    var config = Config(); config.Macros.Clear();
    var loop = new LoopController(config); loop.Start();
    ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.Count == 0, "must not execute any macro");
}

static void RejectInvalidMacro()
{
    foreach (var number in new[] { -1, 100 })
    {
        Reset();
        var config = Config(); config.Macros[0].Number = number;
        var loop = new LoopController(config); loop.Start();
        ExpectState(loop, LoopState.Faulted);
        Expect(config.Macros[0].Number == number, "invalid legacy numbers must not be silently clamped or migrated");
        Expect(loop.Status.Contains("0", StringComparison.Ordinal) && loop.Status.Contains("99", StringComparison.Ordinal), "error must explain the game's valid 0-99 macro range");
        Expect(MacroService.Started.Count == 0, "invalid number must not execute a different macro");
    }
}

static void NativeMacroAddressing()
{
    foreach (var shared in new[] { false, true })
    {
        foreach (var number in new[] { 0, 64, 99 })
        {
            Expect(MacroAddress.IsValidNumber(number), $"visible macro {number} must be valid");
            Expect(MacroAddress.TryCreate(shared, number, out var address), "valid macro address should be created");
            Expect(address != null && address.Set == (shared ? 1u : 0u), "bank mapping must preserve personal/shared selection");
            Expect(address!.Index == (uint)number, $"visible macro {number} must call native slot {number}, without subtracting one");
        }
        foreach (var number in new[] { -1, 100 })
        {
            Expect(!MacroAddress.IsValidNumber(number), $"out-of-range macro {number} must be invalid");
            Expect(!MacroAddress.TryCreate(shared, number, out var address) && address == null, "invalid numbers must never map to a neighboring native slot");
        }
    }
}

static void AcceptMacroBoundaries()
{
    foreach (var shared in new[] { false, true })
    foreach (var number in new[] { 0, 99 })
    {
        Reset();
        var config = Config(); config.Macros[0].Number = number; config.Macros[0].Shared = shared;
        var loop = new LoopController(config); loop.Start();
        ExpectState(loop, LoopState.Decide);
        Tick(loop); Tick(loop);
        ExpectState(loop, LoopState.RunningMacro);
        Expect(MacroService.Started.SequenceEqual([number]), $"controller must pass visible macro {number} unchanged");
        Expect(config.Macros[0].Shared == shared, "controller must not rewrite the macro bank");
    }
}

static void PreserveSavedMacro64()
{
    foreach (var shared in new[] { false, true })
    {
        Reset();
        var config = Config(); config.Macros[0].Number = 64; config.Macros[0].Shared = shared;
        var options = new JsonSerializerOptions { IncludeFields = true };
        var json = JsonSerializer.Serialize(config, options);
        var restored = JsonSerializer.Deserialize<Configuration>(json, options)!;
        Expect(restored.Macros[0].Number == 64 && restored.Macros[0].Shared == shared, "saved macro 64 must retain its number and bank on reload");
        restored.Save();
        var loop = new LoopController(restored); loop.Start(); Tick(loop); Tick(loop);
        ExpectState(loop, LoopState.RunningMacro);
        Expect(MacroService.Started.SequenceEqual([64]), "existing user-entered 64 must execute macro 64 after the fix");
        Expect(restored.Macros[0].Number == 64, "starting must not decrement saved macro numbers");
    }
}

static void DefaultSharedBank()
{
    Reset();
    var step = new MacroStep { Completion = CompletionKind.CraftFinished, ItemId = 100 };
    Expect(step.Shared, "new macro entries must default to the common/shared bank");
    Expect(step.Number == 0, "new macro entries must default to the first visible game slot, zero");
    step.Number = 64;
    var config = Config(); config.Macros = [step];
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(MacroService.Started.SequenceEqual([64]) && step.Shared, "default shared macro 64 must remain 64");
}

static void RejectMissingNavigation()
{
    NavigationService.Available = false;
    var loop = new LoopController(Config()); loop.Start();
    ExpectState(loop, LoopState.Faulted);
    Expect(NavigationService.Destinations.Count == 0, "must not start navigation");
}

static void RejectDifferentTerritory()
{
    var config = Config(); config.TurnInNpc.TerritoryId = 999;
    var loop = new LoopController(config); loop.Start();
    ExpectState(loop, LoopState.Faulted);
}

static void RejectPrematureCraftCompletion()
{
    var config = Config(); config.RecipeId = 42; config.Macros[0].Completion = CompletionKind.MacroFinished;
    var loop = new LoopController(config); loop.Start();
    ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.Count == 0, "must not craft without a final success check");
}

static void SlotThreshold()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; Tick(loop);
    ExpectState(loop, LoopState.ToTurnIn);
    Expect(NavigationService.Destinations.Single().Name == "turnin", "must select submission NPC");
    Reset();
    loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 11; Tick(loop);
    ExpectState(loop, LoopState.PreparingCraft);
}

static void CurrencyThreshold()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Scrips = 9000; Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    Reset();
    loop = new LoopController(Config()); loop.Start();
    InventoryService.Scrips = 8999; Tick(loop);
    ExpectState(loop, LoopState.PreparingCraft);
}

static void RepeatWithTwelveSlots()
{
    var config = Config(); config.RecipeId = 34472; config.Repeat = true;
    config.FreeSlotThreshold = 10; config.Macros[0].Number = 64;
    InventoryService.Slots = 13; InventoryService.Scrips = 7211;
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    MacroService.NextResult = WorkResult.Complete; InventoryService.Slots = 12; Tick(loop);
    Expect(loop.CompletedCycles == 1, "first crafted item must count as one cycle");
    ExpectState(loop, LoopState.Decide); Tick(loop);
    ExpectState(loop, LoopState.PreparingCraft);
    Expect(NavigationService.Destinations.Count == 0, "twelve free slots must not trigger a threshold of ten");
    Tick(loop); ExpectState(loop, LoopState.RunningMacro);
    Expect(MacroService.Started.SequenceEqual([64, 64]), "recipe loop must start the second macro cycle");
}

static void SubmitAtTwelveSlots()
{
    var config = Config(); config.RecipeId = 34472; config.Repeat = true;
    config.FreeSlotThreshold = 12; config.Macros[0].Number = 64;
    InventoryService.Slots = 13; InventoryService.Scrips = 7211;
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    MacroService.NextResult = WorkResult.Complete; InventoryService.Slots = 12; Tick(loop);
    Expect(loop.CompletedCycles == 1, "first crafted item must count as one cycle");
    Tick(loop); ExpectState(loop, LoopState.ToTurnIn);
    Expect(NavigationService.Destinations.Single().Name == "turnin", "inclusive threshold twelve must select the submission NPC");
    Expect(MacroService.Started.SequenceEqual([64]), "submit before starting a second craft when the threshold is reached");
}

static void ExchangePriority()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; InventoryService.Scrips = 9000; Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    Expect(NavigationService.Destinations.Single().Name == "exchange", "must spend tickets before submitting");
}

static void MacroOrdering()
{
    var config = Config();
    config.Macros = [new() { Number = 3 }, new() { Number = 9, Enabled = false }, new() { Number = 7, Completion = CompletionKind.CraftFinished, ItemId = 100 }];
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(MacroService.Started.SequenceEqual([3]), "first enabled macro must start");
    Tick(loop); Tick(loop);
    Expect(MacroService.Started.Count == 1, "pending macro must not advance");
    MacroService.NextResult = WorkResult.Complete; Tick(loop);
    Expect(MacroService.Started.SequenceEqual([3, 7]), "skip disabled macro and keep queue order");
    Tick(loop);
    Expect(loop.CompletedCycles == 1, "one entire macro queue is one cycle");
}

static void MacroFailure()
{
    var config = Config(); config.Macros.Add(new() { Number = 2, Completion = CompletionKind.CraftFinished, ItemId = 100 });
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    MacroService.NextResult = WorkResult.Failed; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.SequenceEqual([1]), "failed macro must prevent later macro");
}

static void ErrandRoundTrip()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; InventoryService.Scrips = 9000;
    Tick(loop); ExpectState(loop, LoopState.ToExchange);
    NavigationService.NextResult = WorkResult.Complete;
    Tick(loop); ExpectState(loop, LoopState.Exchanging);
    FirmamentService.ExchangeResult = WorkResult.Complete; InventoryService.Scrips = 100;
    Tick(loop); ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.ToTurnIn);
    Tick(loop); ExpectState(loop, LoopState.TurningIn);
    FirmamentService.TurnInResult = WorkResult.Complete; InventoryService.Slots = 20;
    Tick(loop); ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.Returning);
    Tick(loop); ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.PreparingCraft);
    Expect(NavigationService.Destinations.Select(x => x.Name).SequenceEqual(["exchange", "turnin", "home"]), "must return home after errands");
}

static void NoTurnInProgress()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    FirmamentService.TurnInResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.Count == 0, "must stop instead of crafting into low inventory indefinitely");
}

static void ExchangeForHeadroom()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    FirmamentService.TurnInResult = WorkResult.Complete;
    FirmamentService.ExchangeRequired = true;
    InventoryService.Scrips = 8990; Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    Expect(NavigationService.Destinations.Last().Name == "exchange", "headroom requirement must override configured threshold");
}

static void RejectNoProgressHeadroomExchange()
{
    var config = Config(); config.ScripThreshold = 9999;
    var loop = new LoopController(config); loop.Start();
    InventoryService.Slots = 10; InventoryService.Scrips = 9850; Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    FirmamentService.TurnInResult = WorkResult.Complete;
    FirmamentService.ExchangeRequired = true; Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    Tick(loop); ExpectState(loop, LoopState.Exchanging);
    FirmamentService.ExchangeResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.Count == 0, "must stop when reward price or reserve prevents all spending");
}

static void CombatStops()
{
    var loop = new LoopController(Config()); loop.Start();
    InventoryService.Slots = 10; Tick(loop);
    var stops = NavigationService.Stops;
    Plugin.Condition[ConditionFlag.InCombat] = true; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(NavigationService.Stops > stops, "must cancel pending navigation");
}

static void TerritoryStops()
{
    var loop = new LoopController(Config()); loop.Start();
    Plugin.ClientState.TerritoryType = 887; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
}

static void StopClearsWork()
{
    var loop = new LoopController(Config()); loop.Start();
    var macroResets = MacroService.Resets; var serviceResets = FirmamentService.Resets;
    loop.Stop();
    ExpectState(loop, LoopState.Stopped);
    Expect(NavigationService.Stops > 0 && MacroService.Resets > macroResets && FirmamentService.Resets > serviceResets, "stop must clean all owned work");
    Tick(loop); Expect(MacroService.Started.Count == 0, "stopped controller must remain inactive");
}

static void OnePassStops()
{
    var config = Config(); config.Repeat = false;
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    MacroService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Stopped);
    Expect(loop.CompletedCycles == 1, "single-pass cycle count");
}
