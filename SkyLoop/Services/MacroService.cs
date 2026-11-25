using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace SkyLoop.Services;

/// <summary>Runs existing user macros. Macro completion and synthesis success are separate checks.</summary>
public sealed unsafe class MacroService
{
    public MacroService(Configuration configuration) { }

    private MacroStep? step;
    private DateTime started;
    private bool ownsMacro;
    private bool observedMacro;
    private string macroName = "";
    private uint watchedItem;
    private int itemBaseline;
    private bool observedCraft;
    private DateTime? craftClosed;

    // Kept across individual macro steps, cleared at the whole sequence boundary by Reset().
    private uint preparedRecipe;
    private uint preparedItem;
    private int preparedBaseline;
    private int prepareStage;
    private DateTime prepareStarted;
    private DateTime nextPrepareAction;
    private bool preparedCraftObserved;

    public string Error { get; private set; } = "";
    public bool IsBusy => step != null || prepareStage != 0;

    public void Start(MacroStep value)
    {
        Error = "";
        step = null;
        if (value.Number is < 1 or > 100)
        {
            Error = "宏编号必须为 1–100。";
            return;
        }
        if (Plugin.ObjectTable.LocalPlayer == null)
        {
            Error = "角色尚未就绪。";
            return;
        }
        var shell = RaptureShellModule.Instance();
        var macros = RaptureMacroModule.Instance();
        if (shell == null || macros == null)
        {
            Error = "无法读取游戏宏模块。";
            return;
        }
        if (shell->MacroCurrentLine >= 0)
        {
            Error = "已有宏正在执行，请等待其结束。";
            return;
        }
        var macro = macros->GetMacro(value.Shared ? 1u : 0u, (uint)(value.Number - 1));
        if (macro == null || macros->GetLineCount(macro) == 0)
        {
            Error = $"{(value.Shared ? "共通" : "个人")}宏 {value.Number} 为空。";
            return;
        }

        watchedItem = value.ItemId;
        if (value.Completion == CompletionKind.CraftFinished && watchedItem == 0)
        {
            watchedItem = preparedItem;
            if (watchedItem == 0)
            {
                var note = RecipeNote.Instance();
                if (note != null && note->CraftingRecipeId != 0)
                    watchedItem = Plugin.DataManager.GetExcelSheet<Recipe>().GetRow(note->CraftingRecipeId).ItemResult.RowId;
            }
        }
        if (value.Completion != CompletionKind.MacroFinished && watchedItem == 0)
        {
            Error = "结果检测需要物品 ID；制作成功检测也可使用已选择配方的成品。";
            return;
        }

        itemBaseline = value.Completion == CompletionKind.CraftFinished && watchedItem == preparedItem
            ? preparedBaseline
            : InventoryService.Count(watchedItem);
        observedCraft = preparedCraftObserved || IsSynthesizing();
        craftClosed = null;
        observedMacro = false;
        macroName = macro->Name.ToString();
        started = DateTime.UtcNow;
        step = value;
        ownsMacro = true;
        shell->ExecuteMacro(macro);
        observedMacro = shell->MacroCurrentLine >= 0;
    }

    public WorkResult Tick()
    {
        if (Error.Length > 0) return WorkResult.Failed;
        if (step == null) return WorkResult.Complete;
        if ((DateTime.UtcNow - started).TotalSeconds > Math.Clamp(step.TimeoutSeconds, 1, 3600))
            return Fail("等待宏或结果超时；请检查宏、材料和制作状态。");
        var shell = RaptureShellModule.Instance();
        if (shell == null) return Fail("游戏宏模块暂不可用。");

        var running = shell->MacroCurrentLine >= 0;
        if (running)
        {
            if (shell->MacroName.ToString() != macroName)
            {
                // Do not cancel the player's replacement macro.
                ownsMacro = false;
                return Fail("宏被其他宏替换，循环已停止。");
            }
            observedMacro = true;
        }
        var crafting = IsSynthesizing();
        observedCraft |= crafting;
        preparedCraftObserved |= crafting;
        if (running || Plugin.Condition[ConditionFlag.ExecutingCraftingAction] || (DateTime.UtcNow - started).TotalMilliseconds < 400)
            return WorkResult.Pending;

        ownsMacro = false;
        if (step.Completion == CompletionKind.MacroFinished)
        {
            if (!observedMacro)
            {
                if ((DateTime.UtcNow - started).TotalSeconds < 2) return WorkResult.Pending;
                return Fail("未观察到游戏接受此宏，已停止。请检查宏锁和角色状态。");
            }
            step = null;
            return WorkResult.Complete;
        }
        var increase = InventoryService.Count(watchedItem) - itemBaseline;
        if (step.Completion == CompletionKind.ItemIncreased)
        {
            if (increase < Math.Max(1, step.MinimumIncrease)) return WorkResult.Pending;
            if (crafting) return WorkResult.Pending;
            step = null;
            return WorkResult.Complete;
        }
        if (crafting) return WorkResult.Pending;
        if (observedCraft && increase >= Math.Max(1, step.MinimumIncrease))
        {
            step = null;
            return WorkResult.Complete;
        }
        if (observedCraft)
        {
            craftClosed ??= DateTime.UtcNow;
            // Inventory updates can arrive shortly after the synthesis addon closes.
            if ((DateTime.UtcNow - craftClosed.Value).TotalSeconds > 4)
                return Fail("制作结束但未检测到成品增加，可能制作失败或被取消。");
        }
        return WorkResult.Pending;
    }

    /// <summary>Opens the configured recipe and starts one normal synthesis before the macro sequence.</summary>
    public WorkResult PrepareCraft(uint recipeId)
    {
        if (recipeId == 0) return WorkResult.Complete;
        if (Error.Length > 0) return WorkResult.Failed;
        if (Plugin.ObjectTable.LocalPlayer == null) return Fail("角色尚未就绪。");
        if (prepareStage == 0)
        {
            var recipes = Plugin.DataManager.GetExcelSheet<Recipe>();
            if (!recipes.TryGetRow(recipeId, out var recipe)) return Fail("所选配方不存在。");
            if (Plugin.PlayerState.ClassJob.RowId != recipe.CraftType.RowId + 8)
                return Fail("当前制作职业与所选配方不一致，请先切换职业。");
            if (IsSynthesizing()) return Fail("当前正在制作，请先结束当前制作。");
            preparedRecipe = recipeId;
            preparedItem = recipe.ItemResult.RowId;
            preparedBaseline = InventoryService.Count(preparedItem);
            preparedCraftObserved = false;
            prepareStarted = DateTime.UtcNow;
            nextPrepareAction = DateTime.MinValue;
            prepareStage = 1;
        }
        if (recipeId != preparedRecipe) return Fail("运行中配方发生变化，请停止后重新开始。");
        if ((DateTime.UtcNow - prepareStarted).TotalSeconds > 20)
            return Fail("打开配方或开始制作超时，请检查材料、职业、状态及制作条件。");
        if (prepareStage == 3)
        {
            if (!IsSynthesizing()) return WorkResult.Pending;
            var active = AgentRecipeNote.Instance();
            if (active != null && active->ActiveCraftRecipeId != 0 && active->ActiveCraftRecipeId != recipeId)
                return Fail("实际制作配方与配置不一致。");
            preparedCraftObserved = true;
            prepareStage = 0;
            return WorkResult.Complete;
        }
        if (DateTime.UtcNow < nextPrepareAction) return WorkResult.Pending;
        var agent = AgentRecipeNote.Instance();
        if (agent == null) return WorkResult.Pending;
        var note = RecipeNote.Instance();
        var addon = (AddonRecipeNote*)Plugin.GameGui.GetAddonByName("RecipeNote", 1).Address;
        if (prepareStage == 1)
        {
            agent->OpenRecipeByRecipeId(recipeId);
            prepareStage = 2;
            nextPrepareAction = DateTime.UtcNow.AddMilliseconds(800);
            return WorkResult.Pending;
        }
        if (addon == null || !addon->IsVisible || note == null || note->RecipeList == null)
            return WorkResult.Pending;
        var selected = note->RecipeList->SelectedRecipe;
        if (selected == null || selected->RecipeId != recipeId || !note->IsRecipeListReady)
            return WorkResult.Pending;
        if (addon->SynthesizeButton == null || !addon->SynthesizeButton->IsEnabled)
            return WorkResult.Pending;
        // Normal synthesis callback, checked against the installed CN API15 client and Artisan.
        AtkValue callback = new() { Type = AtkValueType.Int, Int = 8 };
        addon->FireCallback(1, &callback, true);
        prepareStage = 3; // Never repeat this callback: a failed start should time out safely.
        return WorkResult.Pending;
    }

    public void Reset()
    {
        CancelOwnedMacro();
        step = null;
        prepareStage = 0;
        preparedRecipe = preparedItem = 0;
        preparedCraftObserved = false;
        Error = "";
    }

    private WorkResult Fail(string message)
    {
        Error = message;
        CancelOwnedMacro();
        step = null;
        prepareStage = 0;
        return WorkResult.Failed;
    }

    private void CancelOwnedMacro()
    {
        var shell = RaptureShellModule.Instance();
        if (ownsMacro && shell != null && shell->MacroCurrentLine >= 0 && shell->MacroName.ToString() == macroName)
            shell->CancelMacro();
        ownsMacro = false;
    }

    private static bool IsSynthesizing()
    {
        var synthesis = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("Synthesis", 1).Address;
        return synthesis != null && synthesis->IsVisible;
    }
}
