using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace SkyLoop.Services;

/// <summary>
/// Tick on the framework thread after navigation reaches the NPC. Each transition observes
/// the UI or an inventory/currency change; a delayed callback is never treated as success.
/// </summary>
public sealed unsafe class FirmamentService(Configuration config)
{
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2,
        InventoryType.Inventory3, InventoryType.Inventory4];
    private readonly HashSet<int> triedMenuEntries = [];
    private DateTime nextAction;
    private DateTime lastProgress;
    private bool interacting;
    private string mode = "";
    private int jobIndex = -1;
    private uint selectedSupply;
    private int supplyCountBefore;
    private uint currencyBefore;
    private bool handedOver;
    private bool purchaseIssued;
    private int rewardCountBefore;
    private int purchaseAmount;
    private int categoryAttempt = -1;
    private bool directCategoryTried;
    private RewardOption? selectedReward;

    public string Error { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool NeedsExchange { get; private set; }
    public int CompletedTurnIns { get; private set; }
    public int CompletedPurchases { get; private set; }

    public void Reset()
    {
        mode = "";
        interacting = false;
        nextAction = DateTime.MinValue;
        lastProgress = DateTime.UtcNow;
        selectedSupply = 0;
        jobIndex = -1;
        handedOver = false;
        purchaseIssued = false;
        selectedReward = null;
        triedMenuEntries.Clear();
        categoryAttempt = -1;
        directCategoryTried = false;
        NeedsExchange = false;
        Error = "";
        Status = "";
    }

    public static uint ScripCount()
    {
        var currency = CurrencyManager.Instance();
        return currency == null ? 0 : currency->GetItemCount(RewardCatalog.SkybuildersScripId);
    }

    public static int EligibleCount()
    {
        var total = 0;
        var inventory = InventoryManager.Instance();
        if (inventory == null) return 0;
        foreach (var type in Bags)
        {
            var bag = inventory->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) continue;
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item == null || item->ItemId == 0 || !item->IsCollectable()) continue;
                if (RewardCatalog.Supplies.TryGetValue(item->GetBaseItemId(), out var supply)
                    && item->GetCollectability() >= supply.MinimumRating) total++;
            }
        }
        return total;
    }

    public WorkResult TickTurnIn()
    {
        if (!Begin("turnin")) return WorkResult.Failed;
        if (!WithinDeadline()) return WorkResult.Failed;
        if (DateTime.UtcNow < nextAction) return WorkResult.Pending;
        if (!EnsureNpc(config.TurnInNpc)) return WorkResult.Failed;

        // Only acknowledge a confirmation after our own handover, with the expected item text.
        var yesno = Ready("SelectYesno");
        if (yesno != null)
        {
            if (!handedOver || !ConfirmationNamesItem(yesno, selectedSupply))
                return Fail("出现非预期确认框，已停止提交，请手动处理。");
            if (!ClickButton(yesno, ((AddonSelectYesno*)yesno)->YesButton))
                return WorkResult.Pending;
            Delay();
            return WorkResult.Pending;
        }

        if (handedOver)
        {
            if (SupplyCount(selectedSupply) < supplyCountBefore && ScripCount() > currencyBefore
                && Ready("Request") == null)
            {
                CompletedTurnIns++;
                selectedSupply = 0;
                handedOver = false;
                Progress("提交成功，已核对物品减少和票数增加。");
            }
            return WorkResult.Pending;
        }

        var request = Ready("Request");
        if (request != null)
        {
            if (selectedSupply == 0) return Fail("发现非本次任务打开的提交窗口。");
            if (!RequestNamesItem(request, selectedSupply))
                return Fail("提交窗口要求的物品与所选收藏品不符，已停止。");
            if (HasBelowMinimumRating(selectedSupply))
                return Fail("同名收藏品中含有不达标收藏价值的物品，请先移出这些物品后重试。");
            var button = request->GetComponentButtonById(14);
            if (button != null && button->IsEnabled)
            {
                supplyCountBefore = SupplyCount(selectedSupply);
                currencyBefore = ScripCount();
                if (!ClickButton(request, button)) return WorkResult.Pending;
                handedOver = true;
                Progress("等待服务器确认提交结果。");
                return WorkResult.Pending;
            }
            var picker = Ready("ContextIconMenu");
            if (picker != null)
            {
                if (picker->AtkValuesCount <= 11 || picker->AtkValues[11].Type != AtkValueType.UInt)
                    return Fail("国服物品选择窗口布局不符，停止以避免误点。");
                Fire(picker, 0, 0, picker->AtkValues[11].UInt, 0u);
                Delay();
            }
            else
            {
                Fire(request, 2, 0u, 0u, 0u);
                Delay();
            }
            return WorkResult.Pending;
        }

        var supplyWindow = Ready("HWDSupply");
        if (supplyWindow == null)
        {
            AdvanceTalkOrInteract(config.TurnInNpc);
            return WorkResult.Pending;
        }
        if (selectedSupply != 0) return WorkResult.Pending;

        var supplyOption = NextSupply();
        if (supplyOption == null)
        {
            supplyWindow->Close(true);
            return WorkResult.Complete;
        }
        var current = ScripCount();
        if (current >= config.ScripThreshold || (long)current + supplyOption.MaximumScripReward > config.ScripCap)
        {
            NeedsExchange = true;
            supplyWindow->Close(true);
            return WorkResult.Complete;
        }
        if (jobIndex != supplyOption.JobIndex)
        {
            Fire(supplyWindow, 0, supplyOption.JobIndex);
            jobIndex = supplyOption.JobIndex;
            Progress("切换提交职业分类。");
            return WorkResult.Pending;
        }

        // HWDSupply's row identity sits 18 values after the collectable item id.
        // Check both fields' discriminators before using the client-provided row identity.
        for (var i = 0; i + 18 < supplyWindow->AtkValuesCount; i++)
        {
            var id = supplyWindow->AtkValues[i];
            var row = supplyWindow->AtkValues[i + 18];
            if (id.Type != AtkValueType.UInt || id.UInt != supplyOption.ItemId + 500000
                || row.Type != AtkValueType.UInt || row.UInt >= 100) continue;
            selectedSupply = supplyOption.ItemId;
            supplyCountBefore = SupplyCount(selectedSupply);
            currencyBefore = ScripCount();
            Fire(supplyWindow, 1, (int)row.UInt);
            Progress("打开选定收藏品的提交窗口。");
            return WorkResult.Pending;
        }
        Status = "等待当前职业的收藏品列表；列表不匹配将超时停止。";
        return WorkResult.Pending;
    }

    public WorkResult TickExchange()
    {
        if (!Begin("exchange")) return WorkResult.Failed;
        if (!WithinDeadline()) return WorkResult.Failed;
        if (DateTime.UtcNow < nextAction) return WorkResult.Pending;
        if (!EnsureNpc(config.ExchangeNpc)) return WorkResult.Failed;
        selectedReward ??= RewardCatalog.Items.FirstOrDefault(r => r.ItemId == config.RewardItemId);
        if (selectedReward == null) return Fail("请先从下拉列表选择天穹票兑换物品。");

        var yesno = Ready("SelectYesno");
        if (yesno != null)
        {
            if (!purchaseIssued || !ConfirmationNamesItem(yesno, selectedReward.ItemId))
                return Fail("出现非预期确认框，已停止兑换，请手动处理。");
            ClickButton(yesno, ((AddonSelectYesno*)yesno)->YesButton);
            Delay();
            return WorkResult.Pending;
        }

        if (purchaseIssued)
        {
            var current = ScripCount();
            var amount = InventoryService.Count(selectedReward.ItemId);
            if ((long)currencyBefore - current >= (long)selectedReward.Cost * purchaseAmount
                && (long)amount - rewardCountBefore >= (long)purchaseAmount * selectedReward.ReceiveCount)
            {
                CompletedPurchases += purchaseAmount;
                purchaseIssued = false;
                Progress("兑换成功，已核对票数和物品数量。");
                return WorkResult.Pending;
            }
            var dialog = Ready("ShopExchangeCurrencyDialog");
            if (dialog != null)
            {
                if (ClickButton(dialog, dialog->GetComponentButtonById(17))) Delay();
            }
            return WorkResult.Pending;
        }

        var spendable = Math.Max(0L, (long)ScripCount() - config.ScripReserve);
        var affordable = selectedReward.Cost == 0 ? 0 : spendable / selectedReward.Cost;
        if (affordable == 0)
        {
            CloseOwnedShop();
            return WorkResult.Complete;
        }
        if (selectedReward.Unique && InventoryService.Count(selectedReward.ItemId) > 0)
            return Fail("选定兑换品为独占物品且已持有，请选择可重复兑换的物品。");

        var shop = Ready("ShopExchangeCurrency");
        if (shop != null)
        {
            if (!ReadShop(shop, out var visible))
                return Fail("国服兑换商店布局不匹配，停止以避免兑换错误物品。");
            var row = visible.FirstOrDefault(i => i.ItemId == selectedReward.ItemId);
            if (row.ItemId != 0)
            {
                if (row.Cost != selectedReward.Cost)
                    return Fail("商店显示的价格与游戏数据表不符，已停止兑换。");
                var capacity = RewardCapacity(selectedReward);
                if (capacity <= 0) return Fail("背包没有空间容纳选定兑换品。");
                purchaseAmount = (int)Math.Min(99, Math.Min(affordable, capacity / selectedReward.ReceiveCount));
                if (purchaseAmount <= 0) return Fail("背包剩余容量不足以完成一次兑换。");
                if (selectedReward.Unique || selectedReward.StackSize <= 1) purchaseAmount = 1;
                currencyBefore = ScripCount();
                rewardCountBefore = InventoryService.Count(selectedReward.ItemId);
                purchaseIssued = true;
                Fire(shop, 0, row.Index, purchaseAmount);
                Progress("已请求兑换，等待游戏确认窗口与数量变化。");
                return WorkResult.Pending;
            }

            var otherShop = visible.Select(v => RewardCatalog.Items.FirstOrDefault(i => i.ItemId == v.ItemId)?.ShopId ?? 0)
                .FirstOrDefault(id => id != 0);
            if (otherShop != 0 && otherShop != selectedReward.ShopId)
            {
                shop->Close(true);
                categoryAttempt = -1;
                directCategoryTried = false;
                Delay();
                return WorkResult.Pending;
            }
            if (!directCategoryTried)
            {
                directCategoryTried = true;
                var category = selectedReward.CategoryId > 0 ? selectedReward.CategoryId - 1 : 0;
                Fire(shop, 4, -1, 1, category);
                Delay();
                return WorkResult.Pending;
            }
            categoryAttempt++;
            if (categoryAttempt <= 7)
            {
                Fire(shop, 4, -1, 1, (uint)categoryAttempt);
                Delay();
            }
            else
            {
                shop->Close(true);
                categoryAttempt = -1;
                directCategoryTried = false;
                Delay();
            }
            return WorkResult.Pending;
        }

        var menu = Ready("SelectString");
        if (menu != null)
        {
            var count = ((AddonSelectString*)menu)->PopupMenu.PopupMenu.EntryCount;
            if (count <= 1 || count > 32) return Fail("兑换 NPC 的分类菜单与预期不符。");
            var shops = RewardCatalog.Items.Select(i => i.ShopId).Distinct().OrderBy(i => i).ToList();
            var predicted = shops.IndexOf(selectedReward.ShopId);
            var choice = predicted >= 0 && predicted < count - 1 && !triedMenuEntries.Contains(predicted)
                ? predicted : Enumerable.Range(0, count - 1).FirstOrDefault(i => !triedMenuEntries.Contains(i), -1);
            if (choice < 0) return Fail("已遍历兑换分类，当前 NPC 未出售选定物品。");
            triedMenuEntries.Add(choice);
            Fire(menu, choice);
            Progress("打开天穹票商店分类。");
            return WorkResult.Pending;
        }
        AdvanceTalkOrInteract(config.ExchangeNpc);
        return WorkResult.Pending;
    }

    private bool Begin(string requestedMode)
    {
        if (mode == requestedMode) return Error.Length == 0;
        if (mode.Length != 0) Reset();
        if (Ready("SelectYesno") != null || Ready("Request") != null || Ready("ContextIconMenu") != null
            || Ready("ShopExchangeCurrencyDialog") != null || Ready("Talk") != null || Ready("SelectString") != null
            || Ready("HWDSupply") != null || Ready("ShopExchangeCurrency") != null)
        {
            Error = "开始 NPC 操作前，请先关闭现有对话、提交和商店窗口。";
            return false;
        }
        mode = requestedMode;
        lastProgress = DateTime.UtcNow;
        return true;
    }

    private bool EnsureNpc(NpcLocation expected)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || Plugin.ClientState.TerritoryType != expected.TerritoryId)
        {
            Error = "角色不在已保存的 NPC 区域。";
            return false;
        }
        var npc = Plugin.ObjectTable.FirstOrDefault(o => o.BaseId == expected.DataId);
        if (npc == null || Vector3.Distance(player.Position, npc.Position) > 6)
        {
            Error = "目标 NPC 不在交互距离内，请检查保存的位置和寻路。";
            return false;
        }
        if (interacting && Plugin.TargetManager.Target?.BaseId != expected.DataId)
        {
            Error = "交互目标已改变，已停止 NPC 操作。";
            return false;
        }
        return true;
    }

    private void AdvanceTalkOrInteract(NpcLocation expected)
    {
        var talk = Ready("Talk");
        if (talk != null && interacting)
        {
            var stage = AtkStage.Instance();
            if (stage == null) return;
            var evt = new AtkEvent { Listener = (AtkEventListener*)talk, Target = &stage->AtkEventTarget,
                State = new AtkEventState { StateFlags = (AtkEventStateFlags)132 } };
            var data = new AtkEventData();
            talk->ReceiveEvent(AtkEventType.MouseDown, 0, &evt, &data);
            talk->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
            talk->ReceiveEvent(AtkEventType.MouseUp, 0, &evt, &data);
            Delay();
            return;
        }
        if (interacting) return;
        var npc = Plugin.ObjectTable.FirstOrDefault(o => o.BaseId == expected.DataId);
        var target = TargetSystem.Instance();
        if (npc == null || target == null) return;
        Plugin.TargetManager.Target = npc;
        target->InteractWithObject((NativeObject*)npc.Address, true);
        interacting = true;
        Progress("已与保存的 NPC 交互，等待窗口。");
    }

    private SupplyOption? NextSupply()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        foreach (var type in Bags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) continue;
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item == null || item->ItemId == 0 || !item->IsCollectable()) continue;
                if (RewardCatalog.Supplies.TryGetValue(item->GetBaseItemId(), out var supply)
                    && item->GetCollectability() >= supply.MinimumRating) return supply;
            }
        }
        return null;
    }

    private static long RewardCapacity(RewardOption reward)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return 0;
        long capacity = 0;
        foreach (var type in Bags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) return 0;
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item == null) continue;
                if (item->ItemId == 0) capacity += Math.Max(1, reward.StackSize);
                else if (item->GetBaseItemId() == reward.ItemId && !item->IsHighQuality()
                    && !item->IsCollectable() && item->Quantity < reward.StackSize)
                    capacity += reward.StackSize - item->Quantity;
            }
        }
        return capacity;
    }

    private static bool ReadShop(AtkUnitBase* shop, out List<(uint ItemId, uint Cost, uint Index)> entries)
    {
        entries = [];
        if (shop->AtkValues == null || shop->AtkValuesCount <= 4 || shop->AtkValues[4].Type != AtkValueType.UInt)
            return false;
        var count = shop->AtkValues[4].UInt;
        if (count > 60 || shop->AtkValuesCount < 1310 + count) return false;
        for (var i = 0; i < count; i++)
        {
            var item = shop->AtkValues[1066 + i];
            var cost = shop->AtkValues[456 + i];
            var index = shop->AtkValues[1310 + i];
            if (item.Type != AtkValueType.UInt || cost.Type != AtkValueType.UInt || index.Type != AtkValueType.UInt)
                return false;
            if (item.UInt > 0) entries.Add((item.UInt, cost.UInt, index.UInt));
        }
        return true;
    }

    private static bool ConfirmationNamesItem(AtkUnitBase* addon, uint itemId)
    {
        if (itemId == 0 || addon->AtkValuesCount < 1) return false;
        var prompt = ((AddonSelectYesno*)addon)->PromptText;
        if (prompt == null) return false;
        var name = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(itemId).Name.ExtractText();
        return name.Length > 0 && prompt->NodeText.ToString().Contains(name, StringComparison.Ordinal);
    }

    private static bool RequestNamesItem(AtkUnitBase* addon, uint itemId)
    {
        var name = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(itemId).Name.ExtractText();
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type == AtkValueType.UInt && (value.UInt == itemId || value.UInt == itemId + 500000))
                return true;
            if (value.Type is AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString
                && value.String.Value != null && name.Length > 0
                && MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue.Contains(name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static int SupplyCount(uint itemId)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return 0;
        var count = 0;
        foreach (var type in Bags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) continue;
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item != null && item->ItemId != 0 && item->IsCollectable() && item->GetBaseItemId() == itemId)
                    count += (int)item->Quantity;
            }
        }
        return count;
    }

    private static bool HasBelowMinimumRating(uint itemId)
    {
        if (!RewardCatalog.Supplies.TryGetValue(itemId, out var supply)) return true;
        var manager = InventoryManager.Instance();
        if (manager == null) return true;
        foreach (var type in Bags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded) return true;
            for (var i = 0; i < bag->Size; i++)
            {
                var item = bag->GetInventorySlot(i);
                if (item != null && item->ItemId != 0 && item->IsCollectable()
                    && item->GetBaseItemId() == itemId && item->GetCollectability() < supply.MinimumRating)
                    return true;
            }
        }
        return false;
    }

    private static AtkUnitBase* Ready(string name)
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->IsFullyLoaded() && addon->AtkValues != null ? addon : null;
    }

    private static bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled || button->AtkComponentBase.OwnerNode == null) return false;
        var node = &button->AtkComponentBase.OwnerNode->AtkResNode;
        if (!node->IsVisible()) return false;
        var evt = node->AtkEventManager.Event;
        if (evt == null) return false;
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
        return true;
    }

    private static void Fire(AtkUnitBase* addon, params object[] args)
    {
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is uint number) values[i] = new AtkValue { Type = AtkValueType.UInt, UInt = number };
            else values[i] = new AtkValue { Type = AtkValueType.Int, Int = (int)args[i] };
        }
        addon->FireCallback((uint)args.Length, values, true);
    }

    private void CloseOwnedShop()
    {
        if (!interacting) return;
        var shop = Ready("ShopExchangeCurrency");
        if (shop != null) shop->Close(true);
        var menu = Ready("SelectString");
        if (menu != null) menu->Close(true);
    }

    private bool WithinDeadline()
    {
        if (Error.Length > 0) return false;
        if (DateTime.UtcNow - lastProgress <= TimeSpan.FromSeconds(30)) return true;
        Error = "NPC 操作在 30 秒内未得到预期结果，已停止。最后状态：" + Status;
        return false;
    }

    private void Delay() => nextAction = DateTime.UtcNow.AddMilliseconds(700);
    private void Progress(string status) { Status = status; lastProgress = DateTime.UtcNow; Delay(); }
    private WorkResult Fail(string error) { Error = error; return WorkResult.Failed; }
}
