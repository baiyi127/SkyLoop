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
    private string recipeFilter = "";
    private bool currentJobRecipesOnly = true;
    private string? lastRecipeFilter;
    private uint lastRecipeJob = uint.MaxValue;
    private RecipeOption[] filteredRecipes = [];
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
        if (!string.IsNullOrEmpty(controller.OperationDetail)) ImGui.TextWrapped("步骤：" + controller.OperationDetail);
        ImGui.Text($"背包空格：{controller.FreeSlots} / 140（≤{config.FreeSlotThreshold}格提交）    天穹街振兴票：{controller.Scrips} / {config.ScripCap}    完成轮数：{controller.CompletedCycles}");
        ImGui.Text(controller.NavigationAvailable ? "导航：vnavmesh 已就绪" : "导航：vnavmesh 未就绪（请启用并等待地图加载）");
        ImGui.BeginDisabled(controller.IsRunning);
        if (ImGui.Button("开始循环", new Vector2(130, 30))) plugin.StartLoop();
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
                ImGui.TextWrapped("编号按游戏宏格子显示的 0–99 填写，64 就填 64。个人宏与共通宏分别选择。前段宏可用“宏执行结束”，最后一段建议选“本次制作成功”或“指定物品增加”。");
                changed |= DrawRecipeSelector(config);
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
                    if (ImGui.InputInt("宏编号", ref step.Number)) { step.Number = Math.Clamp(step.Number, MacroAddress.MinimumNumber, MacroAddress.MaximumNumber); changed = true; }
                    var completion = (int)step.Completion;
                    if (ImGui.Combo("成功条件", ref completion, CompletionNames, CompletionNames.Length))
                    {
                        step.Completion = (CompletionKind)completion;
                        if (step.Completion == CompletionKind.CraftFinished) { step.ItemId = 0; step.MinimumIncrease = 1; }
                        changed = true;
                    }
                    if (step.Completion == CompletionKind.CraftFinished)
                    {
                        var recipeRow = config.RecipeId == 0 ? (Recipe?)null : Plugin.DataManager.GetExcelSheet<Recipe>().GetRowOrDefault(config.RecipeId);
                        if (recipeRow.HasValue)
                            ImGui.TextWrapped($"检测配方成品：{recipeRow.Value.ItemResult.Value.Name.ExtractText()}（物品 {recipeRow.Value.ItemResult.RowId}），相对本轮制作前至少增加1件。");
                        else
                            ImGui.TextWrapped("根据本次制作配方检测成品增加；本条件不使用“指定物品增加”中保存的物品ID和数量。");
                    }
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
                changed |= ImGui.Checkbox("库啵好运票已满时继续提交（放弃新好运章）", ref config.ContinueWhenKupoFull);
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
                ImGui.TextWrapped("提交与兑换使用内置的固定交互位置，自动识别当前区域的对应 NPC，无需手动记录。请只记录自己的返回制作点。");
                changed |= DrawLocation("提交 NPC", config.TurnInNpc, true);
                changed |= DrawLocation("兑换 NPC", config.ExchangeNpc, true);
                changed |= DrawLocation("返回制作点", config.CraftLocation, false);
                if (!string.IsNullOrEmpty(captureMessage)) ImGui.TextWrapped(captureMessage);
                ImGui.TextWrapped("采用同区域世界坐标 + 导航网格避障，前往柜台前的固定站位。所有自动提交与兑换均在天穹街完成；区域切换或卡住会停止流程。");
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.EndDisabled();
        if (changed) config.Save();
    }

    private unsafe bool DrawRecipeSelector(Configuration config)
    {
        var selected = RecipeCatalog.Items.FirstOrDefault(option => option.RecipeId == config.RecipeId);
        var preview = config.RecipeId == 0 ? "由宏自行启动制作" : selected == null
            ? $"配方未找到（{config.RecipeId}）" : RecipeLabel(selected);
        var changed = false;
        ImGui.Text("循环制作配方");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##recipe-selection", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.InputText("搜索名称或配方编号", ref recipeFilter, 100);
            ImGui.Checkbox("仅当前制作职业", ref currentJobRecipesOnly);
            var currentJob = Plugin.PlayerState.ClassJob.RowId;
            var jobFilter = currentJobRecipesOnly && currentJob is >= 8 and <= 15 ? currentJob : 0;
            var search = recipeFilter.Trim();
            if (search != lastRecipeFilter || jobFilter != lastRecipeJob)
            {
                filteredRecipes = RecipeCatalog.Items.Where(option => (jobFilter == 0 || option.ClassJobId == jobFilter)
                    && (search.Length == 0 || option.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || option.RecipeId.ToString().Contains(search, StringComparison.Ordinal))).ToArray();
                lastRecipeFilter = search;
                lastRecipeJob = jobFilter;
            }
            if (ImGui.Selectable("由宏自行启动制作", config.RecipeId == 0))
            { config.RecipeId = 0; changed = true; }
            ImGui.TextDisabled($"匹配 {filteredRecipes.Length} 个配方");
            ImGui.Separator();
            if (ImGui.BeginChild("recipe-results", new Vector2(0, 240), false))
            {
                var clipper = ImGui.ImGuiListClipper();
                try
                {
                    clipper.Begin(filteredRecipes.Length, ImGui.GetTextLineHeightWithSpacing());
                    if (ImGui.IsWindowAppearing())
                    {
                        var selectedIndex = Array.FindIndex(filteredRecipes, option => option.RecipeId == config.RecipeId);
                        if (selectedIndex >= 0) clipper.ForceDisplayRangeByIndices(selectedIndex, selectedIndex + 1);
                    }
                    while (clipper.Step())
                    for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                    {
                        var option = filteredRecipes[index];
                        var isSelected = option.RecipeId == config.RecipeId;
                        if (ImGui.Selectable($"{RecipeLabel(option)}##recipe-{option.RecipeId}", isSelected))
                        { config.RecipeId = option.RecipeId; changed = true; }
                        if (isSelected) ImGui.SetItemDefaultFocus();
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"配方编号：{option.RecipeId}；成品物品编号：{option.ResultItemId}");
                    }
                }
                finally { clipper.Destroy(); }
                if (filteredRecipes.Length == 0) ImGui.TextWrapped("没有匹配的配方，请修改搜索词或取消职业筛选。");
            }
            ImGui.EndChild();
            ImGui.EndCombo();
        }
        if (RecipeCatalog.Error.Length > 0) ImGui.TextWrapped(RecipeCatalog.Error);
        return changed;
    }

    private static string RecipeLabel(RecipeOption option) => $"{option.Name}（{option.JobName} Lv.{option.Level}）";

    private bool DrawLocation(string label, NpcLocation location, bool npc)
    {
        ImGui.PushID(label);
        ImGui.Separator();
        ImGui.Text(label + (location.Saved ? $"：{location.Name}（区域 {location.TerritoryId}, NPC {location.DataId}）" : "：尚未记录"));
        ImGui.Text($"{(npc ? "NPC 坐标" : "世界坐标")}：{location.X:F2}, {location.Y:F2}, {location.Z:F2}");
        if (npc)
        {
            ImGui.Text(location.ApproachSaved
                ? $"固定交互位置：{location.ApproachX:F2}, {location.ApproachY:F2}, {location.ApproachZ:F2}"
                : "固定交互位置：等待加载");
            ImGui.PopID();
            return false;
        }
        var changed = false;
        if (ImGui.Button("记录当前位置"))
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) captureMessage = "请先登录角色。";
            else
            {
                var position = player.Position;
                location.Name = label;
                location.DataId = 0;
                location.TerritoryId = Plugin.ClientState.TerritoryType;
                location.X = position.X; location.Y = position.Y; location.Z = position.Z;
                location.Saved = true;
                location.ApproachSaved = false;
                captureMessage = "已记录" + label + "。";
                changed = true;
            }
        }
        ImGui.PopID();
        return changed;
    }
}
