using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SkyLoop.Services;

/// <summary>Known Firmament NPC placements and the verified player interaction spots.</summary>
public static class FirmamentLocations
{
    /// <param name="liveNpcIds">Base IDs of targetable EventNpc objects only; hidden phases can also be loaded.</param>
    public static void Apply(Configuration config, IEnumerable<uint> liveNpcIds)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(liveNpcIds);
        var live = liveNpcIds.ToHashSet();

        // Replace only the NPC locations. In particular, legacy aliases must not change
        // the separately configured crafting point when these fixed spots are applied.
        config.TurnInNpc = Build(config.TurnInNpc, live, "波特金",
            new(1031677, new(52.7582f, -16f, 168.937f)),
            new(1031690, new(50.5531f, -16.0067f, 168.449f)),
            new(47.023697f, -16f, 167.64795f));
        config.ExchangeNpc = Build(config.ExchangeNpc, live, "艾妮",
            new(1031680, new(40.5088f, -16f, 138.504f)),
            new(1031691, new(37.7104f, -16f, 140.136f)),
            new(39.386646f, -16f, 141.85832f));
    }

    private static NpcLocation Build(NpcLocation? previous, IReadOnlySet<uint> live, string name,
        Placement early, Placement late, Vector3 approach)
    {
        var placement = live.Contains(late.DataId) ? late : live.Contains(early.DataId) ? early :
            previous is { Saved: true } && previous.DataId == early.DataId ? early : late;
        return new NpcLocation
        {
            Saved = true,
            Name = name,
            TerritoryId = 886,
            DataId = placement.DataId,
            X = placement.Anchor.X,
            Y = placement.Anchor.Y,
            Z = placement.Anchor.Z,
            ApproachSaved = true,
            ApproachX = approach.X,
            ApproachY = approach.Y,
            ApproachZ = approach.Z,
        };
    }

    private readonly record struct Placement(uint DataId, Vector3 Anchor);
}
