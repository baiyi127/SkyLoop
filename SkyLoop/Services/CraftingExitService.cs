using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SkyLoop.Services;

/// <summary>Closes the normal recipe book and waits until the character can leave crafting.</summary>
public sealed unsafe class CraftingExitService
{
    private const int CloseIntervalMilliseconds = 700;
    private const int MaximumCloseAttempts = 10;
    private bool begun;
    private long startedAt;
    private long nextCloseAt;
    private int closeAttempts;
    private string lastDiagnostic = "";

    public string Status { get; private set; } = "";
    public string Error { get; private set; } = "";

    public void Begin()
    {
        Reset();
        begun = true;
        startedAt = Environment.TickCount64;
        Status = "检查制作状态，准备离开制作位置";
    }

    public WorkResult Tick()
    {
        if (Error.Length > 0) return WorkResult.Failed;
        if (!begun) Begin();
        try
        {
            var now = Environment.TickCount64;
            var elapsed = TimeSpan.FromMilliseconds(now - startedAt);
            var hasPlayer = Plugin.ObjectTable.LocalPlayer != null;
            var book = hasPlayer ? GetVisibleAddon("RecipeNote") : null;
            var synthesisVisible = hasPlayer && GetVisibleAddon("Synthesis") != null;
            var quickSynthesisVisible = hasPlayer && GetVisibleAddon("SynthesisSimple") != null;
            var crafting = hasPlayer && Plugin.Condition[ConditionFlag.Crafting];
            var preparing = hasPlayer && Plugin.Condition[ConditionFlag.PreparingToCraft];
            var executing = hasPlayer && Plugin.Condition[ConditionFlag.ExecutingCraftingAction];
            // Crafting + PreparingToCraft is the ordinary between-crafts posture.
            // A crafting flag without that idle marker can still be an active transition.
            var active = synthesisVisible || quickSynthesisVisible || executing || (crafting && !preparing);
            var snapshot = new CraftExitSnapshot(hasPlayer, active, crafting, preparing, book != null);
            var action = CraftExitDecision.Decide(snapshot, elapsed);
            var diagnostic = $"action={action}, player={hasPlayer}, synthesisVisible={synthesisVisible}, " +
                $"quickSynthesisVisible={quickSynthesisVisible}, executingAction={executing}, crafting={crafting}, " +
                $"preparingToCraft={preparing}, recipeBookVisible={book != null}, closeAttempts={closeAttempts}";

            Status = action switch
            {
                CraftExitAction.Complete => "已退出制作状态，可以前往 NPC",
                CraftExitAction.WaitForActiveCraft => "等待当前制作或制作动画结束，不会中断制作",
                CraftExitAction.CloseRecipeBook => "关闭制作笔记，等待角色退出制作状态",
                CraftExitAction.WaitForIdle => "制作笔记已关闭，等待角色恢复可移动状态",
                CraftExitAction.FailNoPlayer => "角色尚未就绪",
                _ => "退出制作状态超时",
            };
            if (diagnostic != lastDiagnostic)
            {
                lastDiagnostic = diagnostic;
                Plugin.Log.Information($"Craft exit: {diagnostic}, elapsedSeconds={elapsed.TotalSeconds:F2}. {Status}");
            }

            switch (action)
            {
                case CraftExitAction.Complete:
                    return WorkResult.Complete;
                case CraftExitAction.FailNoPlayer:
                    return Fail("角色离线或尚未就绪，已停止退出制作流程。");
                case CraftExitAction.FailTimeout:
                    return Fail("退出制作状态超过 15 秒，尚未开始寻路。最后状态：" + diagnostic);
                case CraftExitAction.CloseRecipeBook:
                    if (now < nextCloseAt || closeAttempts >= MaximumCloseAttempts) return WorkResult.Pending;
                    // Normal recipe-book close command, also used by Artisan's TaskExitCraft.
                    // This branch is unreachable while a synthesis or crafting action is active.
                    AtkValue callback = new() { Type = AtkValueType.Int, Int = -1 };
                    book->FireCallback(1, &callback, true);
                    closeAttempts++;
                    nextCloseAt = now + CloseIntervalMilliseconds;
                    Plugin.Log.Information($"Craft exit: sent normal RecipeNote close callback -1, attempt={closeAttempts}.");
                    return WorkResult.Pending;
                default:
                    return WorkResult.Pending;
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(exception, "Failed to leave crafting state");
            return Fail("读取或关闭制作笔记失败，尚未开始寻路：" + exception.Message);
        }
    }

    public void Reset()
    {
        begun = false;
        startedAt = nextCloseAt = 0;
        closeAttempts = 0;
        lastDiagnostic = "";
        Status = Error = "";
    }

    private static AtkUnitBase* GetVisibleAddon(string name)
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name, 1).Address;
        return addon != null && addon->IsVisible ? addon : null;
    }

    private WorkResult Fail(string message)
    {
        Error = Status = message;
        Plugin.Log.Warning(message);
        return WorkResult.Failed;
    }
}
