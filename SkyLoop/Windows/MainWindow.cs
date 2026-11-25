using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using SkyLoop.Services;

namespace SkyLoop.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string rewardFilter = "";
    private string captureMessage = "";
    private static readonly string[] CompletionNames = ["宏执行结束", "本次制作成功", "指定物品增加"];
    public MainWindow(Plugin plugin) : base("天穹循环 SkyLoop")
    {
        this.plugin = plugin;
        Size = new Vector2(760, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 450), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        var controller = plugin.Controller;
        ImGui.TextWrapped("状态：" + controller.Status);
        ImGui.Text($"背包空格：{controller.FreeSlots} / 140    天穹街振兴票：{controller.Scrips} / {config.ScripCap}    完成轮数：{controller.CompletedCycles}");
        ImGui.Text(controller.NavigationAvailable ? "导航：vnavmesh 已就绪" : "导航：vnavmesh 未就绪（请启用并等待地图加载）");
        ImGui.BeginDisabled(controller.IsRunning);
        if (ImGui.Button("开始循环", new Vector2(130, 30))) controller.Start();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("停止 / 取消宏与移动", new Vector2(190, 30))) controller.Stop();
        ImGui.SameLine();
        if (ImGui.Button("保存配置")) config.Save();
        ImGui.Separator();
        ImGui.BeginDisabled(controller.IsRunning);
        var changed = false;
        if (ImGui.BeginTabBar("configuration"))
        {
            if (ImGui.BeginTabItem("宏队列"))
            {
                ImGui.TextWrapped("编号按游戏宏窗口的 1–100 填写。个人宏与共通宏分别选择。前段宏可用“宏执行结束”，最后一段建议选“本次制作成功”或“指定物品增加”。");
                var recipe = (int)config.RecipeId;
                if (ImGui.InputInt("循环制作配方 ID（0 = 宏自行启动）", ref recipe)) { config.RecipeId = (uint)Math.Max(0, recipe); changed = true; }
                if (config.RecipeId != 0)
                {
                    var row = Plugin.DataManager.GetExcelSheet<Recipe>().GetRowOrDefault(config.RecipeId);
                    ImGui.Text(row.HasValue ? "配方：" + row.Value.ItemResult.Value.Name.ExtractText() : "配方 ID 无效");
                }
                changed |= ImGui.Checkbox("重复整组宏", ref config.Repeat);
                if (ImGui.Button("添加宏")) { config.Macros.Add(new MacroStep()); changed = true; }
                for (var i = 0; i < config.Macros.Count; i++)
                {
                    var step = config.Macros[i];
                    ImGui.PushID(i);
                    ImGui.Separator();
                    ImGui.Text($"第 {i + 1} 步");
                    ImGui.SameLine();
                    changed |= ImGui.Checkbox("启用", ref step.Enabled);
                    ImGui.SameLine();
                    changed |= ImGui.Checkbox("共通宏", ref step.Shared);
                    ImGui.SameLine();
                    if (ImGui.Button("删除")) { config.Macros.RemoveAt(i); ImGui.PopID(); changed = true; break; }
                    ImGui.SameLine();
                    if (i > 0 && ImGui.Button("上移")) { (config.Macros[i - 1], config.Macros[i]) = (config.Macros[i], config.Macros[i - 1]); changed = true; }
                    ImGui.SameLine();
                    if (i + 1 < config.Macros.Count && ImGui.Button("下移")) { (config.Macros[i + 1], config.Macros[i]) = (config.Macros[i], config.Macros[i + 1]); changed = true; }
                    ImGui.SetNextItemWidth(120);
                    if (ImGui.InputInt("宏编号", ref step.Number)) { step.Number = Math.Clamp(step.Number, 1, 100); changed = true; }
                    var completion = (int)step.Completion;
                    if (ImGui.Combo("成功条件", ref completion, CompletionNames, CompletionNames.Length)) { step.Completion = (CompletionKind)completion; changed = true; }
                    if (step.Completion == CompletionKind.ItemIncreased)
                    {
                        var itemId = (int)step.ItemId;
                        if (ImGui.InputInt("检测物品 ID", ref itemId)) { step.ItemId = (uint)Math.Max(0, itemId); changed = true; }
                        if (ImGui.InputInt("最少增加数量", ref step.MinimumIncrease)) { step.MinimumIncrease = Math.Max(1, step.MinimumIncrease); changed = true; }
                    }
                    if (ImGui.InputInt("超时（秒）", ref step.TimeoutSeconds)) { step.TimeoutSeconds = Math.Clamp(step.TimeoutSeconds, 5, 900); changed = true; }
                    ImGui.PopID();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("背包与兑换"))
            {
                changed |= ImGui.Checkbox("背包不足时自动提交重建物品", ref config.AutoTurnIn);
                if (ImGui.InputInt("剩余空格 ≤ 此值时提交", ref config.FreeSlotThreshold)) { config.FreeSlotThreshold = Math.Clamp(config.FreeSlotThreshold, 1, 139); changed = true; }
                changed |= ImGui.Checkbox("票数接近上限时自动兑换", ref config.AutoExchange);
                if (ImGui.InputInt("兑换触发票数", ref config.ScripThreshold)) { config.ScripThreshold = Math.Clamp(config.ScripThreshold, 1, config.ScripCap - 1); changed = true; }
                if (ImGui.InputInt("兑换后至少保留票数", ref config.ScripReserve)) { config.ScripReserve = Math.Clamp(config.ScripReserve, 0, config.ScripThreshold - 1); changed = true; }
                ImGui.TextWrapped("物品列表从当前客户端的天穹街振兴票商店数据读取。兑换按所选物品价格逐次购买，并检查实际扣票和入包结果。");
                ImGui.InputText("筛选兑换物品", ref rewardFilter, 100);
                var selected = RewardCatalog.Items.FirstOrDefault(x => x.ItemId == config.RewardItemId);
                if (ImGui.BeginCombo("兑换物品", selected?.Name ?? "请选择物品"))
                {
                    foreach (var item in RewardCatalog.Items.Where(x => string.IsNullOrWhiteSpace(rewardFilter) || x.Name.Contains(rewardFilter, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (ImGui.Selectable($"{item.Name}（{item.Cost}票）##{item.ShopId}-{item.ShopIndex}", item.ItemId == config.RewardItemId)) { config.RewardItemId = item.ItemId; changed = true; }
                    }
                    ImGui.EndCombo();
                }
                ImGui.Text($"已读取 {RewardCatalog.Items.Count} 个兑换条目");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("NPC 与路线"))
            {
                ImGui.TextWrapped("请在天穹街记录位置。选中正确 NPC 后点“记录目标”；坐标用于寻路，NPC 编号用于到达后确认交互对象。");
                changed |= DrawLocation("提交 NPC", config.TurnInNpc, true);
                changed |= DrawLocation("兑换 NPC", config.ExchangeNpc, true);
                changed |= DrawLocation("返回制作点", config.CraftLocation, false);
                if (!string.IsNullOrEmpty(captureMessage)) ImGui.TextWrapped(captureMessage);
                ImGui.TextWrapped("采用同区域世界坐标 + 导航网格避障。NPC 不在视野内时先前往记录位置，加载后按 NPC 编号定位。区域切换或卡住会停止流程。");
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.EndDisabled();
        if (changed) config.Save();
    }

    private bool DrawLocation(string label, NpcLocation location, bool npc)
    {
        ImGui.PushID(label);
        ImGui.Separator();
        ImGui.Text(label + (location.Saved ? $"：{location.Name}（区域 {location.TerritoryId}, NPC {location.DataId}）" : "：尚未记录"));
        ImGui.Text($"世界坐标：{location.X:F2}, {location.Y:F2}, {location.Z:F2}");
        var changed = false;
        if (ImGui.Button(npc ? "记录当前目标 NPC" : "记录当前位置"))
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            var target = Plugin.TargetManager.Target;
            if (player == null) captureMessage = "请先登录角色。";
            else if (npc && (target == null || target.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)) captureMessage = "请先选中提交或兑换 NPC。";
            else
            {
                var position = npc ? target!.Position : player.Position;
                location.Name = npc ? target!.Name.TextValue : label;
                location.DataId = npc ? target!.BaseId : 0;
                location.TerritoryId = Plugin.ClientState.TerritoryType;
                location.X = position.X; location.Y = position.Y; location.Z = position.Z;
                location.Saved = true;
                captureMessage = "已记录" + label + "。";
                changed = true;
            }
        }
        ImGui.PopID();
        return changed;
    }
}
