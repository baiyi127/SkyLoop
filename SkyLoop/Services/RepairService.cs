using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using NativePlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace SkyLoop.Services;

/// <summary>Self-repairs only equipped gear through the game's normal repair window.</summary>
public sealed unsafe class RepairService
{
    private sealed record Gear(int Slot, uint RawItemId, uint ItemId, ulong Crafter, InventoryItem.ItemFlags Flags,
        ushort Condition, uint RepairJob, int RequiredLevel, int MatterGrade, string Name);
    private List<Gear> equipment = [];
    private List<Gear> critical = [];
    private long started;
    private long phaseStarted;
    private int stage;
    private bool begun;
    private bool selectionSent;
    private bool closeSent;
    private ushort ownedAddonId;
    private Vector3 origin;
    private string readFailure = "";

    public string Error { get; private set; } = "";
    public string Status { get; private set; } = "";

    /// <returns>Raw condition: 30000 is 100%; -1 is unavailable; MaxValue means no repairable gear.</returns>
    public int ReadMinimumCondition()
    {
        try
        {
            var gear = ReadEquipment();
            return gear == null ? -1 : gear.Count == 0 ? int.MaxValue : gear.Min(item => (int)item.Condition);
        }
        catch (Exception exception) { readFailure = exception.Message; return -1; }
    }

    public void Begin()
    {
        Reset();
        begun = true;
        started = phaseStarted = Environment.TickCount64;
        Status = "检查装备耐久、维修职业和暗物质";
    }

    public WorkResult Tick()
    {
        if (Error.Length > 0) return WorkResult.Failed;
        if (!begun) Begin();
        try
        {
            var now = Environment.TickCount64;
            if (now - started > 30000) return Fail("自行维修超过 30 秒，已停止。最后状态：" + Status);
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) return Fail("角色尚未就绪，无法维修。");
            if (Plugin.Condition[ConditionFlag.InCombat] || player.CurrentHp == 0 || Plugin.Condition[ConditionFlag.Mounted])
                return Fail("角色正在战斗、死亡或骑乘，停止维修。");
            if (Plugin.Condition[ConditionFlag.Crafting] || Plugin.Condition[ConditionFlag.PreparingToCraft]
                || Plugin.Condition[ConditionFlag.ExecutingCraftingAction] || Ready("Synthesis") != null || Ready("SynthesisSimple") != null)
                return Fail("当前仍在制作，未执行任何维修操作；请先结束制作。");
            if (stage > 0 && Vector3.DistanceSquared(origin, player.Position) > 0.09f)
                return Fail("维修期间角色发生移动，已停止。");

            var repair = (AddonRepair*)Ready("Repair");
            var yesno = (AddonSelectYesno*)Ready("SelectYesno");
            if (stage == 0)
            {
                var current = ReadEquipment();
                if (current == null) return Fail("装备数据未就绪：" + readFailure);
                equipment = current;
                critical = equipment.Where(item => RepairPolicy.ShouldRepair(item.Condition)).ToList();
                if (critical.Count == 0) { Status = "装备耐久无需维修"; return WorkResult.Complete; }
                if (repair != null || yesno != null) return Fail("已有维修或确认窗口，请先手动关闭后重试。");
                var preflight = Preflight();
                if (preflight.Length != 0) return Fail(preflight);
                origin = player.Position;
                var actions = ActionManager.Instance();
                if (actions == null || actions->GetActionStatus(ActionType.GeneralAction, 6) != 0)
                    return Fail("当前无法使用自行修理指令。");
                if (!actions->UseAction(ActionType.GeneralAction, 6)) return Fail("游戏未接受自行修理指令。");
                Advance(1, "已请求打开自行修理窗口");
                return WorkResult.Pending;
            }

            var snapshot = ReadEquipment();
            if (snapshot == null) return Fail("维修期间装备数据不可用：" + readFailure);
            if (!SameEquipment(snapshot)) return Fail("维修期间装备发生变化，已停止以避免误判。");
            if (stage == 5)
            {
                if (yesno != null) return Fail("关闭维修窗口时出现非预期确认框，请手动处理。");
                if (repair == null) { Status = "自行维修完成，已核对装备耐久恢复"; return WorkResult.Complete; }
                if (repair->Id != ownedAddonId) return Fail("维修窗口归属发生变化。");
                if (!closeSent) { repair->Close(true); closeSent = true; }
                return WorkResult.Pending;
            }
            if (repair == null)
            {
                if (stage == 1 && now - phaseStarted <= 6000) return WorkResult.Pending;
                return Fail("自行修理窗口未打开或被关闭。");
            }
            if (ownedAddonId == 0) ownedAddonId = repair->Id;
            if (repair->Id != ownedAddonId) return Fail("维修窗口归属发生变化。");
            var agent = AgentRepair.Instance();
            if (agent == null || !agent->IsSelfRepairOpen || !agent->UseSelfRepair)
            {
                if (stage == 1 && now - phaseStarted < 4000) return WorkResult.Pending;
                return Fail("当前窗口不是本次自行修理，已停止。");
            }

            if (stage == 1 || stage == 2)
            {
                if (yesno != null) return Fail("选择装备分类前出现非本次操作的确认框。");
                var dropdown = repair->Dropdown;
                if (dropdown == null) return Fail("维修窗口缺少装备分类下拉框。");
                if (agent->Filter != AgentRepair.ItemFilter.Equipped || dropdown->GetSelectedItemIndex() != 0)
                {
                    if (!selectionSent)
                    {
                        dropdown->SelectItem(0);
                        selectionSent = true;
                        Advance(2, "选择装备中分类，等待游戏确认");
                        return WorkResult.Pending;
                    }
                    if (now - phaseStarted > 4000) return Fail("装备中分类未被游戏确认，请在维修窗口手动选择“装备中”后重试。");
                    return WorkResult.Pending;
                }
                if (agent->IsAddonRefreshPending) return WorkResult.Pending;
                var preflight = Preflight();
                if (preflight.Length != 0) return Fail(preflight);
                if (repair->RepairAllButton == null || !repair->RepairAllButton->IsEnabled)
                {
                    if (now - phaseStarted > 6000) return Fail("全部修理按钮不可用，请检查职业等级和暗物质。");
                    return WorkResult.Pending;
                }
                if (!Click(&repair->AtkUnitBase, repair->RepairAllButton)) return Fail("全部修理按钮没有可用的正常点击事件。");
                Advance(3, "已点击当前装备全部修理，等待本次确认框");
                return WorkResult.Pending;
            }

            if (agent->Filter != AgentRepair.ItemFilter.Equipped || repair->Dropdown == null
                || repair->Dropdown->GetSelectedItemIndex() != 0)
                return Fail("维修期间装备分类发生变化。");
            // Repairing and receiving the server result both legitimately refresh this list.
            if (agent->IsAddonRefreshPending) return WorkResult.Pending;
            if (stage == 3)
            {
                if (yesno == null)
                {
                    if (now - phaseStarted > 8000) return Fail("本次全部修理没有出现可确认窗口。");
                    return WorkResult.Pending;
                }
                if (agent->DialogAddonId <= 0)
                {
                    if (now - phaseStarted > 8000) return Fail("未能确认本次维修确认框的归属，请手动处理。");
                    Status = "等待本次维修确认框的归属数据";
                    return WorkResult.Pending;
                }
                if (agent->DialogAddonId != yesno->Id)
                    return Fail("确认框不属于本次维修操作，请手动处理。");
                var text = yesno->PromptText == null ? "" : yesno->PromptText->NodeText.ToString();
                if (!RepairPolicy.IsSelfRepairConfirmation(text))
                    return Fail("确认内容不是当前装备自行修理；拒绝金币修理或全部分页维修。");
                if (!Click(&yesno->AtkUnitBase, yesno->YesButton)) return WorkResult.Pending;
                Advance(4, "已确认自行维修，等待实际装备耐久恢复");
                return WorkResult.Pending;
            }
            if (yesno != null)
            {
                if (now - phaseStarted < 1500) return WorkResult.Pending;
                return Fail("维修确认框未关闭，请手动检查。");
            }
            var repaired = critical.All(item => snapshot.Any(current => current.Slot == item.Slot
                && current.Condition > item.Condition && current.Condition >= RepairPolicy.TriggerCondition));
            if (!repaired)
            {
                if (now - phaseStarted > 12000) return Fail("维修后仍有原低耐久装备未恢复，可能材料不足或维修失败。");
                return WorkResult.Pending;
            }
            Advance(5, "原低耐久装备已全部恢复，关闭本次维修窗口");
            return WorkResult.Pending;
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(exception, "Self repair failed");
            return Fail("自行维修接口异常：" + exception.Message);
        }
    }

    public void Reset()
    {
        equipment = [];
        critical = [];
        stage = 0;
        begun = selectionSent = closeSent = false;
        ownedAddonId = 0;
        started = phaseStarted = 0;
        Error = Status = readFailure = "";
    }

    private List<Gear>? ReadEquipment()
    {
        readFailure = "";
        if (Plugin.ObjectTable.LocalPlayer == null) { readFailure = "角色未就绪"; return null; }
        var manager = InventoryManager.Instance();
        var container = manager == null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null || !container->IsLoaded || container->Size is < 1 or > 64)
        { readFailure = "装备栏未加载"; return null; }
        var result = new List<Gear>();
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        for (var slot = 0; slot < container->Size; slot++)
        {
            var item = container->GetInventorySlot(slot);
            if (item == null) { readFailure = $"装备栏位 {slot} 尚未就绪"; return null; }
            if (item->ItemId == 0) continue;
            var id = item->GetBaseItemId();
            if (!items.TryGetRow(id, out var row)) { readFailure = $"装备物品 {id} 不存在"; return null; }
            if (row.ClassJobRepair.RowId == 0) continue;
            result.Add(new Gear(slot, item->ItemId, id, item->CrafterContentId, item->Flags, item->Condition,
                row.ClassJobRepair.RowId, Math.Max(1, row.LevelEquip - 10), (int)row.ItemRepair.RowId, row.Name.ExtractText()));
        }
        return result;
    }

    private string Preflight()
    {
        var state = NativePlayerState.Instance();
        if (state == null) return "无法读取已练维修职业等级。";
        var resources = Plugin.DataManager.GetExcelSheet<ItemRepairResource>();
        var available = new Dictionary<int, int>();
        foreach (var resource in resources)
            if (resource.RowId > 0 && resource.Item.RowId > 0)
                available[(int)resource.RowId] = InventoryService.Count(resource.Item.RowId);
        var required = new List<int>();
        foreach (var item in equipment.Where(item => item.Condition < 30000))
        {
            var level = state->GetClassJobLevel((int)item.RepairJob, false);
            if (level < item.RequiredLevel || item.MatterGrade <= 0)
            {
                if (critical.Any(low => low.Slot == item.Slot))
                {
                    var jobName = Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(item.RepairJob, out var job)
                        ? job.Name.ExtractText() : item.RepairJob.ToString();
                    return $"无法自行维修“{item.Name}”：需要{jobName}等级 {item.RequiredLevel}，当前 {level}，并具备对应暗物质。";
                }
                continue;
            }
            required.Add(item.MatterGrade);
        }
        return RepairPolicy.TryAllocateDarkMatter(required, available, out _)
            ? "" : "暗物质不足以修理当前装备中的所有可修装备，停止以免低耐久装备只修复一部分。";
    }

    private bool SameEquipment(List<Gear> current) => current.Count == equipment.Count && equipment.All(item =>
        current.Any(other => other.Slot == item.Slot && other.RawItemId == item.RawItemId && other.ItemId == item.ItemId
            && other.Crafter == item.Crafter && other.Flags == item.Flags));

    private static AtkUnitBase* Ready(string name)
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name, 1).Address;
        return addon != null && addon->IsVisible && addon->IsFullyLoaded() ? addon : null;
    }

    private static bool Click(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled || button->AtkComponentBase.OwnerNode == null) return false;
        var node = &button->AtkComponentBase.OwnerNode->AtkResNode;
        var evt = node->AtkEventManager.Event;
        if (!node->IsVisible() || evt == null) return false;
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
        return true;
    }

    private void Advance(int nextStage, string status)
    {
        stage = nextStage;
        phaseStarted = Environment.TickCount64;
        Status = status;
        Plugin.Log.Information($"Self repair: stage={stage}, addon={ownedAddonId}, critical={critical.Count}. {status}");
    }

    private WorkResult Fail(string error)
    {
        Error = Status = error;
        Plugin.Log.Warning($"Self repair failed: stage={stage}, addon={ownedAddonId}. {error}");
        return WorkResult.Failed;
    }
}
