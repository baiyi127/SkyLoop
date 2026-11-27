using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace SkyLoop;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<MacroStep> Macros = [];
    public int FreeSlotThreshold = 10;
    public int ScripThreshold = 9000;
    public int ScripReserve = 0;
    public int ScripCap = 10000;
    public uint ScripItemId = 28063;
    public uint RecipeId;
    public uint RewardItemId;
    public bool AutoTurnIn = true;
    public bool ContinueWhenKupoFull = true;
    public bool AutoExchange = true;
    public bool Repeat = true;
    public NpcLocation TurnInNpc = new() { Name = "提交 NPC" };
    public NpcLocation ExchangeNpc = new() { Name = "兑换 NPC" };
    public NpcLocation CraftLocation = new() { Name = "制作位置" };
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

public enum CompletionKind { MacroFinished, CraftFinished, ItemIncreased }
public enum WorkResult { Pending, Complete, Failed }

[Serializable]
public sealed class MacroStep
{
    public bool Enabled = true;
    public bool Shared = true;
    public int Number = 0;
    public CompletionKind Completion;
    public uint ItemId;
    public int MinimumIncrease = 1;
    public int TimeoutSeconds = 180;
}

[Serializable]
public sealed class NpcLocation
{
    public bool Saved;
    public string Name = "";
    public uint TerritoryId;
    public uint DataId;
    public float X;
    public float Y;
    public float Z;
    public Vector3 Position => new(X, Y, Z);
    // NPC identity/placement and the player's reachable interaction spot are distinct.
    // The Firmament preset supplies the fixed counter-side interaction spots.
    public bool ApproachSaved;
    public float ApproachX;
    public float ApproachY;
    public float ApproachZ;
    public Vector3 ApproachPosition => new(ApproachX, ApproachY, ApproachZ);
}
