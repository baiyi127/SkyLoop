using System;

namespace SkyLoop.Services;

/// <summary>Chooses what a macro's completion condition actually observes.</summary>
public readonly record struct MacroResultTarget(uint ItemId, bool UsePreparedBaseline)
{
    public static MacroResultTarget Resolve(CompletionKind kind, uint configuredItemId,
        uint preparedResultItemId, uint activeRecipeResultItemId) => kind switch
    {
        // ItemId belongs to ItemIncreased. A hidden value left by that mode must not
        // replace the actual product of the current craft (recipe IDs are not item IDs).
        CompletionKind.CraftFinished when preparedResultItemId != 0 => new(preparedResultItemId, true),
        CompletionKind.CraftFinished => new(activeRecipeResultItemId, false),
        CompletionKind.ItemIncreased => new(configuredItemId, false),
        _ => new(0, false),
    };

    /// <summary>Split macros share the inventory snapshot taken before the whole craft.</summary>
    public int GetBaseline(int preparedBaseline, int currentCount) =>
        UsePreparedBaseline ? preparedBaseline : currentCount;

    public static int RequiredIncrease(CompletionKind kind, int configuredMinimum) => kind switch
    {
        CompletionKind.CraftFinished => 1,
        CompletionKind.ItemIncreased => Math.Max(1, configuredMinimum),
        _ => 0,
    };
}
