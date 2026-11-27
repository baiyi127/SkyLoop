namespace SkyLoop.Services;

public enum TurnInRequestMatch { Unknown, Match, Mismatch }

/// <summary>A single read of the active NPC trade request, without pointers or UI text.</summary>
public readonly record struct TurnInRequestSnapshot(
    bool AgentActive,
    bool AddonMatches,
    int NativeCount,
    int AddonEntryCount,
    uint ItemId,
    int RequiredQuantity,
    bool WantCollectible,
    bool RequestReady = true);

public static class TurnInRequestValidator
{
    public static TurnInRequestMatch Evaluate(uint expectedItemId, in TurnInRequestSnapshot request)
    {
        // Empty or inconsistent data occurs while Request is being populated. It does not
        // establish that another item was requested and must never authorize a callback.
        if (expectedItemId == 0 || !request.RequestReady || !request.AgentActive || !request.AddonMatches
            || request.NativeCount is < 1 or > 5 || request.AddonEntryCount is < 1 or > 5
            || request.NativeCount != request.AddonEntryCount)
            return TurnInRequestMatch.Unknown;

        // Firmament turns in one collectable at a time. A coherent multi-item request is
        // an actual different workflow, rather than a partially loaded Firmament request.
        if (request.NativeCount != 1) return TurnInRequestMatch.Mismatch;
        if (request.ItemId == 0 || request.RequiredQuantity <= 0) return TurnInRequestMatch.Unknown;
        if (BaseItemId(request.ItemId) != expectedItemId || request.RequiredQuantity != 1 || !request.WantCollectible)
            return TurnInRequestMatch.Mismatch;
        return TurnInRequestMatch.Match;
    }

    public static bool CanSubmit(uint expectedItemId, in TurnInRequestSnapshot request)
        => Evaluate(expectedItemId, request) == TurnInRequestMatch.Match;

    private static uint BaseItemId(uint itemId)
    {
        // Keep event/unknown IDs intact so they cannot accidentally match a normal item.
        if (itemId is >= 1_000_000 and < 2_000_000) return itemId - 1_000_000;
        if (itemId is >= 500_000 and < 1_000_000) return itemId - 500_000;
        return itemId;
    }
}
