namespace SkyLoop.Services;

/// <summary>Reuses an already selected recipe without reopening its native window.</summary>
public static class CraftPreparationDecision
{
    public static bool CanReuseSelectedRecipe(uint requestedRecipeId, bool windowVisible,
        bool listReady, uint selectedRecipeId) => requestedRecipeId != 0
        && windowVisible && listReady && selectedRecipeId == requestedRecipeId;
}
