using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using SkyLoop.Services;

namespace SkyLoop;

public enum LoopState { Stopped, Decide, PreparingCraft, RunningMacro, ToTurnIn, TurningIn, ToExchange, Exchanging, Returning, Faulted }

public sealed class LoopController
{
    private readonly Configuration config;
    private readonly MacroService macros;
    private readonly NavigationService navigation;
    private readonly FirmamentService firmament;
    private List<MacroStep> queue = [];
    private int macroIndex;
    private uint territory;
    private long nextTick;
    private bool away;
    private int exchangeStartScrips;
    public LoopState State { get; private set; }
    public bool IsRunning => State is not (LoopState.Stopped or LoopState.Faulted);
    public string Status { get; private set; } = "已停止";
    public int CompletedCycles { get; private set; }
    public int CurrentMacro => macroIndex + 1;
    public int FreeSlots { get; private set; } = -1;
    public int Scrips { get; private set; }
    public bool NavigationAvailable => navigation.IsAvailable;

    public LoopController(Configuration config)
    {
        this.config = config;
        macros = new MacroService(config);
        navigation = new NavigationService();
        firmament = new FirmamentService(config);
    }

    public void Start()
    {
        if (IsRunning) return;
        queue = config.Macros.Where(x => x.Enabled).ToList();
        if (Plugin.ObjectTable.LocalPlayer == null) { Fail("请先登录角色。"); return; }
        if (queue.Count == 0) { Fail("请先添加至少一个启用的宏。"); return; }
        if (config.RecipeId != 0 && queue[^1].Completion == CompletionKind.MacroFinished) { Fail("循环制作时，最后一个宏请选择“本次制作成功”或“指定物品增加”。"); return; }
        if (queue.Any(x => !MacroAddress.IsValidNumber(x.Number) || x.TimeoutSeconds < 5 || x.MinimumIncrease < 1 || x.Completion == CompletionKind.ItemIncreased && x.ItemId == 0)) { Fail("宏编号须为游戏格子显示的0–99；请检查完成条件与超时。"); return; }
        if (config.FreeSlotThreshold is < 1 or > 139 || config.ScripThreshold <= 0 || config.ScripThreshold >= config.ScripCap || config.ScripReserve < 0 || config.ScripReserve >= config.ScripThreshold) { Fail("请检查背包、票数阈值和保留票数。"); return; }
        if ((config.AutoTurnIn || config.AutoExchange) && (!config.CraftLocation.Saved || !navigation.IsAvailable)) { Fail("请记录制作位置并启用 vnavmesh。"); return; }
        if (config.AutoTurnIn && !config.TurnInNpc.Saved) { Fail("请记录提交 NPC。"); return; }
        if (config.AutoExchange && (!config.ExchangeNpc.Saved || config.RewardItemId == 0)) { Fail("请记录兑换 NPC 并选择兑换物品。"); return; }
        territory = Plugin.ClientState.TerritoryType;
        if (new[] { config.CraftLocation, config.TurnInNpc, config.ExchangeNpc }.Any(x => x.Saved && x.TerritoryId != territory)) { Fail("记录位置与当前区域不同；请在天穹街重新记录。"); return; }
        CompletedCycles = 0;
        macroIndex = 0;
        away = false;
        macros.Reset();
        firmament.Reset();
        config.Save();
        SetState(LoopState.Decide, "检查背包和票数");
    }

    public void Stop()
    {
        navigation.Stop();
        macros.Reset();
        firmament.Reset();
        SetState(LoopState.Stopped, "已停止");
    }

    public void Update()
    {
        var now = Environment.TickCount64;
        if (now < nextTick) return;
        nextTick = now + 100;
        try
        {
            FreeSlots = InventoryService.FreeSlots();
            Scrips = Plugin.ObjectTable.LocalPlayer == null ? 0 : (int)FirmamentService.ScripCount();
            if (!IsRunning) return;
            if (Plugin.ObjectTable.LocalPlayer == null || Plugin.ClientState.TerritoryType != territory || Plugin.ObjectTable.LocalPlayer.CurrentHp == 0 || Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51]) { Fail("角色离线、战斗、死亡或切换区域，流程已停止。"); return; }
            if (FreeSlots < 0) { Fail("背包数据尚未加载。"); return; }
            switch (State)
            {
                case LoopState.Decide:
                    if (config.AutoExchange && Scrips >= config.ScripThreshold) { Travel(config.ExchangeNpc, LoopState.ToExchange, "前往兑换 NPC"); return; }
                    if (config.AutoTurnIn && FreeSlots <= config.FreeSlotThreshold) { Travel(config.TurnInNpc, LoopState.ToTurnIn, "前往提交 NPC"); return; }
                    if (FreeSlots == 0) { Fail("背包已满，无法继续制作。"); return; }
                    if (away) { Travel(config.CraftLocation, LoopState.Returning, "返回制作位置"); return; }
                    macroIndex = 0;
                    SetState(LoopState.PreparingCraft, "准备制作 / 宏队列");
                    break;
                case LoopState.PreparingCraft:
                    var prepare = config.RecipeId == 0 ? WorkResult.Complete : macros.PrepareCraft(config.RecipeId);
                    if (prepare == WorkResult.Failed) { Fail(macros.Error); return; }
                    if (prepare == WorkResult.Complete) BeginMacro();
                    break;
                case LoopState.RunningMacro:
                    var result = macros.Tick();
                    if (result == WorkResult.Failed) { Fail(macros.Error); return; }
                    if (result == WorkResult.Complete)
                    {
                        macroIndex++;
                        if (macroIndex < queue.Count) BeginMacro();
                        else
                        {
                            CompletedCycles++;
                            macros.Reset();
                            if (!config.Repeat) Stop();
                            else SetState(LoopState.Decide, "本轮完成，检查背包和票数");
                        }
                    }
                    break;
                case LoopState.ToTurnIn: FinishTravel(LoopState.TurningIn, "提交重建物品"); break;
                case LoopState.ToExchange: FinishTravel(LoopState.Exchanging, "兑换所选物品"); break;
                case LoopState.Returning:
                    if (FinishTravel(LoopState.Decide, "已返回，继续循环")) away = false;
                    break;
                case LoopState.TurningIn:
                    var turnin = firmament.TickTurnIn();
                    if (turnin == WorkResult.Failed) Fail(firmament.Error);
                    else if (turnin == WorkResult.Complete)
                    {
                        var needsExchange = firmament.NeedsExchange;
                        firmament.Reset();
                        if (needsExchange && config.AutoExchange) Travel(config.ExchangeNpc, LoopState.ToExchange, "票数余量不足，先前往兑换");
                        else if (needsExchange) Fail("票数不足以容纳下一件提交奖励，请兑换后继续。");
                        else if (FreeSlots <= config.FreeSlotThreshold && (!config.AutoExchange || Scrips < config.ScripThreshold)) Fail("提交结束后空格仍不足；请检查是否有可提交的重建收藏品。");
                        else SetState(LoopState.Decide, "提交完成，检查票数");
                    }
                    break;
                case LoopState.Exchanging:
                    var exchange = firmament.TickExchange();
                    if (exchange == WorkResult.Failed) Fail(firmament.Error);
                    else if (exchange == WorkResult.Complete)
                    {
                        firmament.Reset();
                        if (Scrips >= config.ScripThreshold || Scrips >= exchangeStartScrips) Fail("兑换未能降低票数；检查物品价格、保留票数、持有上限与背包空间。");
                        else SetState(LoopState.Decide, "兑换完成");
                    }
                    break;
            }
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "SkyLoop tick failed"); Fail("接口异常，流程停止：" + ex.Message); }
    }

    private void BeginMacro()
    {
        macros.Start(queue[macroIndex]);
        SetState(LoopState.RunningMacro, $"等待宏 {macroIndex + 1}/{queue.Count}（{(queue[macroIndex].Shared ? "共通" : "个人")} {queue[macroIndex].Number}）完成");
    }

    private void Travel(NpcLocation destination, LoopState state, string message)
    {
        firmament.Reset();
        navigation.Start(destination);
        if (state == LoopState.ToExchange) exchangeStartScrips = Scrips;
        away = true;
        SetState(state, message);
    }

    private bool FinishTravel(LoopState state, string message)
    {
        var result = navigation.Tick();
        if (result == WorkResult.Failed) Fail(navigation.Error);
        else if (result == WorkResult.Complete) { SetState(state, message); return true; }
        return false;
    }

    private void Fail(string message)
    {
        navigation.Stop();
        macros.Reset();
        firmament.Reset();
        SetState(LoopState.Faulted, message);
        Plugin.Log.Warning(message);
        Plugin.ChatGui.PrintError("[SkyLoop] " + message);
    }

    private void SetState(LoopState state, string status) { State = state; Status = status; }
}
