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
    ("navigation stays queued until crafting exit completes", NavigationWaitsForCraftingExit),
    ("failed crafting exit prevents navigation", FailedCraftingExitPreventsNavigation),
    ("stopping crafting exit cancels queued navigation permanently", StopCancelsQueuedNavigation),
    ("combat while leaving crafting cancels queued navigation", CombatCancelsQueuedNavigation),
    ("already idle crafting exit starts navigation immediately", IdleExitStartsNavigation),
    ("exchange navigation also waits for crafting exit", ExchangeWaitsForCraftingExit),
    ("macro queue waits and advances strictly in configured order", MacroOrdering),
    ("failed macro prevents the following macro", MacroFailure),
    ("exchange and submission return home before crafting", ErrandRoundTrip),
    ("submission without reclaimed space stops the loop", NoTurnInProgress),
    ("insufficient currency headroom exchanges before the threshold", ExchangeForHeadroom),
    ("headroom exchange must actually spend currency", RejectNoProgressHeadroomExchange),
    ("65 to 25 is successful with trigger 20 and price 40", LowThresholdExchangeSuccess),
    ("a trigger below price never sends an unaffordable exchange trip", UnaffordableTriggerContinues),
    ("reserved tickets are excluded from the next exchange trigger", ReservedRemainderContinues),
    ("cap headroom cannot force an unaffordable exchange trip", RejectUnaffordableHeadroom),
    ("unknown zero-price and unreachable rewards stop before crafting", RejectInvalidRewardChoices),
    ("earlier purchases cannot satisfy a later zero-progress exchange", PriorPurchasesAreNotNewProgress),
    ("last submission can exchange before low-slot validation stops", LastSubmissionCanExchange),
    ("self repair triggers below ten percent but not at ten percent", RepairThresholdBoundary),
    ("self repair waits for crafting exit before beginning", RepairWaitsForCraftingExit),
    ("failed crafting exit prevents self repair", FailedExitPreventsRepair),
    ("failed self repair prevents further crafting and navigation", FailedRepairStopsFlow),
    ("completed self repair resumes the normal crafting cycle", CompletedRepairResumesFlow),
    ("stop cancels self repair queued behind crafting exit", StopCancelsQueuedRepair),
    ("stop resets active repair and ignores late completion", StopCancelsActiveRepair),
    ("combat cancels repair queued behind crafting exit", CombatCancelsQueuedRepair),
    ("combat resets active repair before another repair tick", CombatCancelsActiveRepair),
    ("disabled self repair ignores low or unavailable durability", DisabledRepairDoesNotInterrupt),
    ("unavailable equipment condition stops enabled self repair", UnavailableRepairConditionStops),
    ("no repairable equipment does not request repair", NoRepairableEquipmentContinues),
    ("repair is checked between complete macro groups", RepairDoesNotInterruptMacroGroup),
    ("low durability takes priority over exchange and submission", RepairHasPriorityOverErrands),
    ("repair away from home preserves the pending return trip", RepairPreservesAwayState),
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
    RewardCatalog.Items.Clear();
    RewardCatalog.Items.Add(new(123, 40));
    MacroService.NextResult = WorkResult.Pending;
    MacroService.PrepareResult = WorkResult.Complete;
    MacroService.Started.Clear();
    MacroService.Resets = 0;
    NavigationService.Available = true;
    NavigationService.NextResult = WorkResult.Pending;
    NavigationService.Destinations.Clear();
    NavigationService.Stops = 0;
    CraftingExitService.NextResult = WorkResult.Complete;
    CraftingExitService.Begins = 0;
    CraftingExitService.Ticks = 0;
    CraftingExitService.Resets = 0;
    RepairService.MinimumCondition = 30000;
    RepairService.NextResult = WorkResult.Pending;
    RepairService.Reads = 0;
    RepairService.Begins = 0;
    RepairService.Ticks = 0;
    RepairService.Resets = 0;
    FirmamentService.TurnInResult = WorkResult.Pending;
    FirmamentService.ExchangeResult = WorkResult.Pending;
    FirmamentService.ExchangeRequired = false;
    FirmamentService.VerifiedPurchases = 0;
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

static void NavigationWaitsForCraftingExit()
{
    CraftingExitService.NextResult = WorkResult.Pending;
    InventoryService.Slots = 10;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingCrafting);
    Expect(CraftingExitService.Begins == 1 && NavigationService.Destinations.Count == 0,
        "request crafting exit before sending a path to vnavmesh");
    Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.LeavingCrafting);
    Expect(NavigationService.Destinations.Count == 0 && MacroService.Started.Count == 0,
        "neither navigation nor another macro can start while the character is still crafting");
    CraftingExitService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.ToTurnIn);
    Expect(NavigationService.Destinations.Count == 1 && NavigationService.Destinations[0].Name == "turnin",
        "the exact queued destination must start once exit is confirmed");
    Tick(loop);
    Expect(NavigationService.Destinations.Count == 1, "subsequent updates must not issue the queued path again");
}

static void FailedCraftingExitPreventsNavigation()
{
    CraftingExitService.NextResult = WorkResult.Failed;
    InventoryService.Slots = 10;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(NavigationService.Destinations.Count == 0, "failed exit must not execute any path");
    Expect(loop.Status.Contains("craft exit failure", StringComparison.Ordinal), "keep the exit failure reason visible");
}

static void StopCancelsQueuedNavigation()
{
    CraftingExitService.NextResult = WorkResult.Pending;
    InventoryService.Slots = 10;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingCrafting);
    var resets = CraftingExitService.Resets;
    loop.Stop(); ExpectState(loop, LoopState.Stopped);
    Expect(CraftingExitService.Resets > resets, "stop must reset the crafting exit operation");
    CraftingExitService.NextResult = WorkResult.Complete; Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.Stopped);
    Expect(NavigationService.Destinations.Count == 0, "a late exit completion must never start the canceled path");
}

static void CombatCancelsQueuedNavigation()
{
    CraftingExitService.NextResult = WorkResult.Pending;
    InventoryService.Slots = 10;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    var resets = CraftingExitService.Resets;
    Plugin.Condition[ConditionFlag.InCombat] = true; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(CraftingExitService.Resets > resets && NavigationService.Destinations.Count == 0,
        "combat must clear the pending exit and prevent navigation");
}

static void IdleExitStartsNavigation()
{
    CraftingExitService.NextResult = WorkResult.Complete;
    InventoryService.Slots = 10;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.ToTurnIn);
    Expect(CraftingExitService.Begins == 1 && CraftingExitService.Ticks == 1
        && NavigationService.Destinations.Count == 1, "an already idle character can begin the path in the same update");
}

static void ExchangeWaitsForCraftingExit()
{
    CraftingExitService.NextResult = WorkResult.Pending;
    InventoryService.Scrips = 9000;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingCrafting);
    Expect(NavigationService.Destinations.Count == 0, "exchange path must not start while crafting flags remain active");
    CraftingExitService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    Expect(NavigationService.Destinations.Single().Name == "exchange", "preserve the queued exchange destination");
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
    FirmamentService.VerifiedPurchases = 1;
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

static void LowThresholdExchangeSuccess()
{
    var config = Config(); config.ScripThreshold = 20;
    InventoryService.Scrips = 65;
    var loop = new LoopController(config); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.ToExchange);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Exchanging);
    InventoryService.Scrips = 25;
    FirmamentService.VerifiedPurchases = 1;
    FirmamentService.ExchangeResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.Returning);
    Tick(loop); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(NavigationService.Destinations.Select(x => x.Name).SequenceEqual(["exchange", "home"]),
        "a verified purchase must resume crafting without a second unaffordable exchange trip");
}

static void UnaffordableTriggerContinues()
{
    var config = Config(); config.ScripThreshold = 20;
    InventoryService.Scrips = 25;
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(NavigationService.Destinations.Count == 0, "25 tickets cannot buy the selected 40-ticket item");
    Reset();
    InventoryService.Scrips = 25; InventoryService.Slots = 10;
    loop = new LoopController(config); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.ToTurnIn);
    Expect(NavigationService.Destinations.Single().Name == "turnin", "earn tickets by submission instead of visiting an unaffordable shop");
}

static void ReservedRemainderContinues()
{
    var config = Config(); config.ScripThreshold = 40; config.ScripReserve = 30;
    InventoryService.Scrips = 65;
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(NavigationService.Destinations.Count == 0, "the 30 reserved tickets leave only 35 spendable, despite balance exceeding both trigger and price");
}

static void RejectUnaffordableHeadroom()
{
    var config = Config(); config.ScripThreshold = 9999;
    RewardCatalog.Items.Clear(); RewardCatalog.Items.Add(new(123, 10000));
    InventoryService.Scrips = 9950; InventoryService.Slots = 10;
    var loop = new LoopController(config); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.ToTurnIn);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    FirmamentService.ExchangeRequired = true; FirmamentService.TurnInResult = WorkResult.Complete;
    Tick(loop); ExpectState(loop, LoopState.Faulted);
    Expect(loop.Status.Contains("买不起", StringComparison.Ordinal), "report a cap/affordability conflict instead of claiming a failed purchase");
    Expect(NavigationService.Destinations.All(x => x.Name != "exchange"), "do not route to the shop without enough spendable tickets");
}

static void RejectInvalidRewardChoices()
{
    foreach (var scenario in new[] { "unknown", "zero-price", "reserve-impossible" })
    {
        Reset();
        var config = Config();
        if (scenario == "unknown") config.RewardItemId = 999;
        if (scenario == "zero-price") { RewardCatalog.Items.Clear(); RewardCatalog.Items.Add(new(123, 0)); }
        if (scenario == "reserve-impossible")
        {
            config.ScripReserve = 2000;
            RewardCatalog.Items.Clear(); RewardCatalog.Items.Add(new(123, 8400));
        }
        var loop = new LoopController(config); loop.Start(); Tick(loop);
        ExpectState(loop, LoopState.Faulted);
        Expect(MacroService.Started.Count == 0 && NavigationService.Destinations.Count == 0,
            $"{scenario} must not craft indefinitely with an unusable automatic exchange setting");
    }
}

static void PriorPurchasesAreNotNewProgress()
{
    var config = Config(); config.ScripThreshold = 20;
    InventoryService.Scrips = 65; FirmamentService.VerifiedPurchases = 7;
    var loop = new LoopController(config); loop.Start(); Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    InventoryService.Scrips = 25; FirmamentService.ExchangeResult = WorkResult.Complete;
    Tick(loop); ExpectState(loop, LoopState.Faulted);
    Expect(MacroService.Started.Count == 0, "currency changes without a new verified purchase do not prove this exchange succeeded");
}

static void LastSubmissionCanExchange()
{
    var config = Config(); config.ScripThreshold = 20;
    InventoryService.Slots = 10;
    var loop = new LoopController(config); loop.Start(); Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    InventoryService.Scrips = 65; FirmamentService.TurnInResult = WorkResult.Complete;
    Tick(loop); ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.ToExchange);
    Expect(NavigationService.Destinations.Select(x => x.Name).SequenceEqual(["turnin", "exchange"]),
        "an affordable reward should still exchange after the final turn-in, even if carried items keep slots low");
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

static void ReachRepairing(LoopController loop)
{
    for (var attempt = 0; attempt < 4 && loop.State != LoopState.Repairing; attempt++) Tick(loop);
    ExpectState(loop, LoopState.Repairing);
}

static void RepairThresholdBoundary()
{
    foreach (var condition in new[] { 2997, 2999 })
    {
        Reset();
        var config = Config();
        Expect(config.AutoRepair, "automatic self repair must be enabled by default");
        RepairService.MinimumCondition = condition;
        var lowConditionLoop = new LoopController(config); lowConditionLoop.Start();
        Expect(RepairService.Resets > 0, "start must clear earlier repair operations");
        ReachRepairing(lowConditionLoop);
        Expect(RepairService.Begins == 1 && MacroService.Started.Count == 0,
            $"condition {condition}/30000 below 10% must repair before starting a macro");
    }
    Reset();
    RepairService.MinimumCondition = 3000;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.PreparingCraft);
    Expect(RepairService.Begins == 0 && CraftingExitService.Begins == 0,
        "exactly 10% must not trigger the strictly-below threshold");
}

static void RepairWaitsForCraftingExit()
{
    RepairService.MinimumCondition = 2999;
    CraftingExitService.NextResult = WorkResult.Pending;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingForRepair);
    Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.LeavingForRepair);
    Expect(CraftingExitService.Begins == 1 && RepairService.Begins == 0 && RepairService.Ticks == 0,
        "repair may neither begin nor execute while the character is still crafting");
    Expect(MacroService.Started.Count == 0 && NavigationService.Destinations.Count == 0,
        "do not start other work while leaving crafting for repair");
    CraftingExitService.NextResult = WorkResult.Complete; ReachRepairing(loop); Tick(loop);
    Expect(RepairService.Begins == 1, "confirmed crafting exit must begin self repair exactly once");
}

static void FailedExitPreventsRepair()
{
    RepairService.MinimumCondition = 2999;
    CraftingExitService.NextResult = WorkResult.Failed;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    if (loop.State == LoopState.LeavingForRepair) Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(RepairService.Begins == 0 && RepairService.Ticks == 0,
        "an unsuccessful crafting exit cannot authorize a repair action");
    Expect(MacroService.Started.Count == 0 && NavigationService.Destinations.Count == 0,
        "failed exit stops all subsequent work");
}

static void FailedRepairStopsFlow()
{
    RepairService.MinimumCondition = 2999;
    var loop = new LoopController(Config()); loop.Start(); ReachRepairing(loop);
    var resets = RepairService.Resets;
    RepairService.NextResult = WorkResult.Failed; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(loop.Status.Contains("repair failure", StringComparison.Ordinal), "preserve the self repair failure reason");
    Expect(RepairService.Resets > resets && MacroService.Started.Count == 0 && NavigationService.Destinations.Count == 0,
        "a failed repair must reset owned repair work and prevent crafting or travel");
}

static void CompletedRepairResumesFlow()
{
    RepairService.MinimumCondition = 2999;
    var loop = new LoopController(Config()); loop.Start(); ReachRepairing(loop);
    RepairService.MinimumCondition = 30000; RepairService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Decide);
    Expect(loop.CompletedCycles == 0, "repair must not count as a completed macro cycle");
    Tick(loop); ExpectState(loop, LoopState.PreparingCraft);
    Tick(loop); ExpectState(loop, LoopState.RunningMacro);
    Expect(RepairService.Begins == 1 && MacroService.Started.SequenceEqual([1]),
        "successful self repair must resume the ordinary macro queue once");
}

static void StopCancelsQueuedRepair()
{
    RepairService.MinimumCondition = 2999; CraftingExitService.NextResult = WorkResult.Pending;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingForRepair);
    var resets = RepairService.Resets;
    loop.Stop(); ExpectState(loop, LoopState.Stopped);
    Expect(RepairService.Resets > resets, "stop must clear the repair operation even before it begins");
    CraftingExitService.NextResult = WorkResult.Complete; RepairService.NextResult = WorkResult.Complete;
    Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.Stopped);
    Expect(RepairService.Begins == 0 && RepairService.Ticks == 0,
        "late crafting exit must not begin the canceled repair");
}

static void StopCancelsActiveRepair()
{
    RepairService.MinimumCondition = 2999;
    var loop = new LoopController(Config()); loop.Start(); ReachRepairing(loop);
    var resets = RepairService.Resets;
    loop.Stop();
    var ticks = RepairService.Ticks;
    Expect(RepairService.Resets > resets, "stop must reset active self repair");
    RepairService.MinimumCondition = 30000; RepairService.NextResult = WorkResult.Complete;
    Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.Stopped);
    Expect(RepairService.Ticks == ticks && MacroService.Started.Count == 0,
        "late repair completion must not poll repair or restart crafting after stop");
}

static void CombatCancelsQueuedRepair()
{
    RepairService.MinimumCondition = 2999; CraftingExitService.NextResult = WorkResult.Pending;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.LeavingForRepair);
    var resets = RepairService.Resets;
    Plugin.Condition[ConditionFlag.InCombat] = true; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(RepairService.Resets > resets && RepairService.Begins == 0,
        "combat must cancel queued self repair before any repair action");
    Plugin.Condition[ConditionFlag.InCombat] = false;
    CraftingExitService.NextResult = WorkResult.Complete; RepairService.NextResult = WorkResult.Complete; Tick(loop);
    Expect(RepairService.Begins == 0 && MacroService.Started.Count == 0,
        "leaving combat must not revive the canceled repair or macro queue");
}

static void DisabledRepairDoesNotInterrupt()
{
    foreach (var condition in new[] { 2999, -1 })
    {
        Reset();
        var config = Config(); config.AutoRepair = false;
        RepairService.MinimumCondition = condition;
        var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
        ExpectState(loop, LoopState.RunningMacro);
        Expect(RepairService.Begins == 0 && CraftingExitService.Begins == 0,
            "disabled self repair must not block crafting on low or unavailable condition");
    }
}

static void CombatCancelsActiveRepair()
{
    RepairService.MinimumCondition = 2999;
    var loop = new LoopController(Config()); loop.Start(); ReachRepairing(loop);
    var resets = RepairService.Resets;
    var ticks = RepairService.Ticks;
    Plugin.Condition[ConditionFlag.InCombat] = true; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(RepairService.Resets > resets && RepairService.Ticks == ticks,
        "combat must reset repair before permitting another repair action");
    Plugin.Condition[ConditionFlag.InCombat] = false;
    RepairService.MinimumCondition = 30000; RepairService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(RepairService.Begins == 1 && MacroService.Started.Count == 0,
        "late completion after combat must not start another repair or macro");
}

static void UnavailableRepairConditionStops()
{
    RepairService.MinimumCondition = -1;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    ExpectState(loop, LoopState.Faulted);
    Expect(RepairService.Begins == 0 && MacroService.Started.Count == 0 && NavigationService.Destinations.Count == 0,
        "unknown equipment data must not be treated as healthy or start repair blindly");
}

static void NoRepairableEquipmentContinues()
{
    RepairService.MinimumCondition = int.MaxValue;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop); Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(RepairService.Begins == 0, "an empty repairable-equipment set must not trigger self repair");
}

static void RepairDoesNotInterruptMacroGroup()
{
    var config = Config(); config.RecipeId = 34472;
    config.Macros = [new() { Number = 64 }, new() { Number = 0, Completion = CompletionKind.CraftFinished }];
    var loop = new LoopController(config); loop.Start(); Tick(loop); Tick(loop);
    RepairService.MinimumCondition = 2999;
    Tick(loop); ExpectState(loop, LoopState.RunningMacro);
    Expect(RepairService.Begins == 0, "low condition must not interrupt a running macro");
    MacroService.NextResult = WorkResult.Complete; Tick(loop);
    Expect(MacroService.Started.SequenceEqual([64, 0]) && RepairService.Begins == 0,
        "a split craft must finish its remaining macro before any repair");
    MacroService.NextResult = WorkResult.Pending; Tick(loop);
    ExpectState(loop, LoopState.RunningMacro);
    Expect(RepairService.Begins == 0, "do not repair in the middle of the macro group");
    MacroService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Decide);
    Expect(loop.CompletedCycles == 1, "finish the complete craft before repair");
    ReachRepairing(loop);
    Expect(MacroService.Started.SequenceEqual([64, 0]), "repair must not restart either macro from the completed group");
    RepairService.MinimumCondition = 30000; RepairService.NextResult = WorkResult.Complete; Tick(loop);
    Tick(loop); Tick(loop);
    Expect(MacroService.Started.SequenceEqual([64, 0, 64]), "after repair the next full group starts normally");
}

static void RepairHasPriorityOverErrands()
{
    RepairService.MinimumCondition = 2999; InventoryService.Slots = 10; InventoryService.Scrips = 9000;
    var loop = new LoopController(Config()); loop.Start(); ReachRepairing(loop);
    Expect(RepairService.Begins == 1 && NavigationService.Destinations.Count == 0,
        "repair must be chosen before an otherwise due exchange or submission trip");
}

static void RepairPreservesAwayState()
{
    InventoryService.Scrips = 9000;
    var loop = new LoopController(Config()); loop.Start(); Tick(loop);
    NavigationService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Exchanging);
    InventoryService.Scrips = 100; FirmamentService.VerifiedPurchases = 1;
    FirmamentService.ExchangeResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Decide);
    RepairService.MinimumCondition = 2999; ReachRepairing(loop);
    Expect(NavigationService.Destinations.Select(x => x.Name).SequenceEqual(["exchange"]),
        "repair in place before resuming the pending trip home");
    RepairService.MinimumCondition = 30000; RepairService.NextResult = WorkResult.Complete; Tick(loop);
    ExpectState(loop, LoopState.Decide);
    Tick(loop); ExpectState(loop, LoopState.Returning);
    Expect(NavigationService.Destinations.Select(x => x.Name).SequenceEqual(["exchange", "home"])
        && MacroService.Started.Count == 0, "repair must preserve away=true and return home before crafting");
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
