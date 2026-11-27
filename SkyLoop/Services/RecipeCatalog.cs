using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace SkyLoop.Services;

public sealed record RecipeOption(uint RecipeId, uint ResultItemId, string Name,
    uint ClassJobId, string JobName, int Level);

/// <summary>Caches this client's recipes, product names, crafting jobs and required levels once.</summary>
public static class RecipeCatalog
{
    private static readonly object SyncRoot = new();
    private static readonly string[] JobNames =
        ["刻木匠", "锻铁匠", "铸甲匠", "雕金匠", "制革匠", "裁衣匠", "炼金术士", "烹调师"];
    private static bool loaded;

    public static IReadOnlyList<RecipeOption> Items { get; private set; } = [];
    public static string Error { get; private set; } = "";

    public static void Load()
    {
        lock (SyncRoot)
        {
            if (loaded) return;
            // A failed load also remains cached until plugin reload, avoiding repeated sheet errors every frame.
            loaded = true;
            try
            {
                var recipes = Plugin.DataManager.GetExcelSheet<Recipe>();
                var items = Plugin.DataManager.GetExcelSheet<Item>();
                var levels = Plugin.DataManager.GetExcelSheet<RecipeLevelTable>();
                var jobs = Plugin.DataManager.GetExcelSheet<ClassJob>();
                var jobNames = new string[JobNames.Length];
                for (var index = 0; index < jobNames.Length; index++)
                {
                    jobNames[index] = JobNames[index];
                    if (jobs.TryGetRow((uint)index + 8, out var job))
                    {
                        var localizedName = job.Name.ExtractText();
                        if (!string.IsNullOrWhiteSpace(localizedName)) jobNames[index] = localizedName;
                    }
                }

                var options = new List<RecipeOption>();
                var missingRows = 0;
                foreach (var recipe in recipes)
                {
                    var resultItemId = recipe.ItemResult.RowId;
                    if (recipe.RowId == 0 || resultItemId == 0) continue;
                    var craftType = recipe.CraftType.RowId;
                    if (craftType >= jobNames.Length || !items.TryGetRow(resultItemId, out var item))
                    {
                        missingRows++;
                        continue;
                    }
                    var name = item.Name.ExtractText();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var level = 0;
                    if (levels.TryGetRow(recipe.RecipeLevelTable.RowId, out var recipeLevel))
                        level = recipeLevel.ClassJobLevel;
                    // Keep distinct recipes producing the same item; their job or requirements can differ.
                    options.Add(new RecipeOption(recipe.RowId, resultItemId, name,
                        craftType + 8, jobNames[craftType], level));
                }
                Items = options.OrderBy(option => option.Name, StringComparer.CurrentCulture)
                    .ThenBy(option => option.ClassJobId).ThenBy(option => option.Level)
                    .ThenBy(option => option.RecipeId).ToArray();
                Error = Items.Count == 0 ? "未读取到有效的制作配方。" : "";
                Plugin.Log.Information($"Recipe catalog loaded: recipes={Items.Count}, unavailableRows={missingRows}.");
                if (missingRows > 0)
                    Plugin.Log.Warning($"Skipped {missingRows} recipe rows with missing products or invalid crafting jobs.");
            }
            catch (Exception exception)
            {
                Error = "读取国服制作配方失败：" + exception.Message;
                Plugin.Log.Error(exception, "Could not load recipe catalog from this client's sheets");
            }
        }
    }
}
