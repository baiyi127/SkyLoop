using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace SkyLoop.Services;

public sealed record RewardOption(uint ItemId, string Name, uint Cost, uint ShopId, int ShopIndex,
    uint CategoryId, uint ReceiveCount, uint StackSize, bool Unique);

public sealed record SupplyOption(uint ItemId, int JobIndex, ushort MinimumRating, uint MaximumScripReward);

/// <summary>Builds the menus from this client's sheets, including its Chinese item names.</summary>
public static class RewardCatalog
{
    public const uint SkybuildersScripId = 28063;
    public static IReadOnlyList<RewardOption> Items { get; private set; } = [];
    public static IReadOnlyDictionary<uint, SupplyOption> Supplies { get; private set; } =
        new Dictionary<uint, SupplyOption>();
    public static string Error { get; private set; } = "";

    public static void Load()
    {
        try
        {
            var items = Plugin.DataManager.GetExcelSheet<Item>();
            var wares = new List<RewardOption>();
            foreach (var shop in Plugin.DataManager.GetExcelSheet<SpecialShop>())
            {
                var shopIndex = 0;
                foreach (var entry in shop.Item)
                {
                    // Skip mixed-currency and multi-reward deals: one configured reward has one exact price.
                    var costs = entry.ItemCosts.Where(c => c.CurrencyCost > 0).ToArray();
                    var receives = entry.ReceiveItems.Where(r => r.Item.RowId > 0 && r.ReceiveCount > 0).ToArray();
                    if (costs.Length == 1 && costs[0].ItemCost.RowId == SkybuildersScripId && receives.Length == 1)
                    {
                        var receive = receives[0];
                        var item = items.GetRow(receive.Item.RowId);
                        wares.Add(new RewardOption(item.RowId, item.Name.ExtractText(), costs[0].CurrencyCost,
                            shop.RowId, shopIndex, entry.Category.Count > 0 ? entry.Category[0].RowId : 0,
                            receive.ReceiveCount, item.StackSize, item.IsUnique));
                    }
                    shopIndex++;
                }
            }
            Items = wares.GroupBy(w => w.ItemId).Select(g => g.OrderBy(w => w.ShopId).First())
                .OrderBy(w => w.Name, StringComparer.CurrentCulture).ToArray();

            var jobByItem = new Dictionary<uint, int>();
            foreach (var recipe in Plugin.DataManager.GetExcelSheet<Recipe>())
                if (recipe.ItemResult.RowId > 0)
                    jobByItem.TryAdd(recipe.ItemResult.RowId, (int)recipe.CraftType.RowId);
            var supplies = new Dictionary<uint, SupplyOption>();
            foreach (var supply in Plugin.DataManager.GetExcelSheet<HWDCrafterSupply>())
            foreach (var entry in supply.HWDCrafterSupplyParams)
            {
                var itemId = entry.ItemTradeIn.RowId;
                if (itemId == 0 || !jobByItem.TryGetValue(itemId, out var job)) continue;
                uint maximum = 0;
                uint[] rewardRows = [entry.BaseCollectableReward.RowId, entry.MidCollectableReward.RowId,
                    entry.HighCollectableReward.RowId, entry.BaseCollectableRewardPostPhase.RowId,
                    entry.MidCollectableRewardPostPhase.RowId, entry.HighCollectableRewardPostPhase.RowId];
                var rewards = Plugin.DataManager.GetExcelSheet<HWDCrafterSupplyReward>();
                foreach (var id in rewardRows)
                    if (id != 0) maximum = Math.Max(maximum, rewards.GetRow(id).ScriptRewardAmount);
                supplies[itemId] = new SupplyOption(itemId, job, entry.BaseCollectableRating, maximum);
            }
            Supplies = supplies;
            Error = "";
        }
        catch (Exception ex)
        {
            Error = "读取国服物品表失败：" + ex.Message;
            Plugin.Log.Error(ex, "Could not load Firmament sheets");
        }
    }

    public static void ApplyCurrencyDefaults(Configuration config)
    {
        try
        {
            var scrip = Plugin.DataManager.GetExcelSheet<Item>().GetRow(SkybuildersScripId);
            if (scrip.StackSize > 0 && scrip.StackSize <= int.MaxValue)
                config.ScripCap = (int)scrip.StackSize;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not resolve the Skybuilders' scrip cap");
        }
    }
}
