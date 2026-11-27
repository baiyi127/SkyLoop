using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace Dalamud.Game.ClientState.Conditions
{
    public enum ConditionFlag { InCombat, BetweenAreas, BetweenAreas51, Crafting, ExecutingCraftingAction }
}

namespace SkyLoop
{
    public static class Plugin
    {
        public static FakeObjectTable ObjectTable { get; } = new();
        public static FakeClientState ClientState { get; } = new();
        public static FakeCondition Condition { get; } = new();
        public static FakeLog Log { get; } = new();
        public static FakeChat ChatGui { get; } = new();
        public static FakePluginInterface PluginInterface { get; } = new();
    }

    public sealed class FakeObjectTable { public FakePlayer? LocalPlayer { get; set; } = new(); }
    public sealed class FakePlayer { public uint CurrentHp { get; set; } = 1; }
    public sealed class FakeClientState { public uint TerritoryType { get; set; } = 886; }
    public sealed class FakeCondition
    {
        private readonly Dictionary<ConditionFlag, bool> values = [];
        public bool this[ConditionFlag flag] { get => values.GetValueOrDefault(flag); set => values[flag] = value; }
        public void Clear() => values.Clear();
    }
    public sealed class FakeLog
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(Exception exception, string message) { }
    }
    public sealed class FakeChat { public void PrintError(string message) { } }
    public sealed class FakePluginInterface { public void SavePluginConfig(object value) { } }
}

namespace SkyLoop.Services
{
    public static class InventoryService
    {
        public static int Slots = 20;
        public static int Scrips;
        public static int FreeSlots() => Slots;
        public static int Count(uint itemId) => Scrips;
    }

    public sealed class MacroService
    {
        public MacroService(Configuration config) { }
        public static WorkResult NextResult = WorkResult.Pending;
        public static WorkResult PrepareResult = WorkResult.Complete;
        public static List<int> Started = [];
        public static int Resets;
        public string Error => "macro failure";
        public void Start(MacroStep step) => Started.Add(step.Number);
        public WorkResult Tick() => NextResult;
        public WorkResult PrepareCraft(uint recipeId) => PrepareResult;
        public void Reset() => Resets++;
    }

    public sealed class NavigationService
    {
        public static bool Available = true;
        public static WorkResult NextResult = WorkResult.Pending;
        public static List<NpcLocation> Destinations = [];
        public static int Stops;
        public string Error => "navigation failure";
        public bool IsAvailable => Available;
        public void Start(NpcLocation location) => Destinations.Add(location);
        public WorkResult Tick() => NextResult;
        public void Stop() => Stops++;
    }

    public sealed class FirmamentService
    {
        public FirmamentService(Configuration config) { }
        public static WorkResult TurnInResult = WorkResult.Pending;
        public static WorkResult ExchangeResult = WorkResult.Pending;
        public static int Resets;
        public static bool ExchangeRequired;
        public bool NeedsExchange => ExchangeRequired;
        public static uint ScripCount() => (uint)InventoryService.Scrips;
        public string Error => "Firmament failure";
        public string Status => "fake Firmament status";
        public WorkResult TickTurnIn() => TurnInResult;
        public WorkResult TickExchange() => ExchangeResult;
        public void Reset() => Resets++;
    }
}
