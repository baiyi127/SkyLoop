using System;

namespace SkyLoop.Services;

public readonly record struct CraftExitSnapshot(bool PlayerAvailable, bool CraftActive,
    bool Crafting, bool Preparing, bool RecipeBookVisible);

public enum CraftExitAction
{
    Complete,
    WaitForActiveCraft,
    CloseRecipeBook,
    WaitForIdle,
    FailNoPlayer,
    FailTimeout,
}

/// <summary>Exits the between-crafts posture without interrupting an active synthesis.</summary>
public static class CraftExitDecision
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static CraftExitAction Decide(CraftExitSnapshot snapshot, TimeSpan elapsed)
    {
        if (!snapshot.PlayerAvailable) return CraftExitAction.FailNoPlayer;
        if (!snapshot.CraftActive && !snapshot.Crafting && !snapshot.Preparing && !snapshot.RecipeBookVisible)
            return CraftExitAction.Complete;
        if (elapsed >= Timeout) return CraftExitAction.FailTimeout;
        if (snapshot.CraftActive) return CraftExitAction.WaitForActiveCraft;
        if (snapshot.RecipeBookVisible) return CraftExitAction.CloseRecipeBook;
        return CraftExitAction.WaitForIdle;
    }
}
