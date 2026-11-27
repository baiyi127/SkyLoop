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
    private int requiredIncrease;
    private uint watchedRecipe;
    private string resultSource = "none";
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
    private string lastPrepareSignature = "";

    public string Error { get; private set; } = "";
    public string Progress { get; private set; } = "";
    public string Status => Progress;
    public bool IsBusy => step != null || prepareStage != 0;

    public void Start(MacroStep value)
    {
        Error = "";
        step = null;
        if (!MacroAddress.TryCreate(value.Shared, value.Number, out var address))
        {
            Error = "宏编号必须为 0–99，与游戏宏界面显示的编号一致。";
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
        var macro = macros->GetMacro(address.Set, address.Index);
        if (macro == null || macros->GetLineCount(macro) == 0)
        {
            Error = $"{(value.Shared ? "共通" : "个人")}宏 {value.Number} 为空。";
            return;
        }

        uint activeResultItem = 0;
        watchedRecipe = 0;
        if (value.Completion == CompletionKind.CraftFinished)
        {
            watchedRecipe = preparedRecipe;
            if (preparedItem == 0)
            {
                var note = RecipeNote.Instance();
                if (note != null && note->CraftingRecipeId != 0)
                {
                    watchedRecipe = note->CraftingRecipeId;
                    if (Plugin.DataManager.GetExcelSheet<Recipe>().TryGetRow(watchedRecipe, out var activeRecipe))
                        activeResultItem = activeRecipe.ItemResult.RowId;
                }
            }
        }
        var resultTarget = MacroResultTarget.Resolve(value.Completion, value.ItemId, preparedItem, activeResultItem);
        watchedItem = resultTarget.ItemId;
        requiredIncrease = MacroResultTarget.RequiredIncrease(value.Completion, value.MinimumIncrease);
        resultSource = resultTarget.UsePreparedBaseline ? "prepared recipe" :
            value.Completion == CompletionKind.CraftFinished ? "active recipe" :
            value.Completion == CompletionKind.ItemIncreased ? "explicit item" : "macro interpreter";
        if (value.Completion != CompletionKind.MacroFinished && watchedItem == 0)
        {
            Error = value.Completion == CompletionKind.CraftFinished
                ? "无法识别本次制作的成品，请设置循环配方 ID，或先开始制作再执行宏。"
                : "指定物品增加需要填写物品 ID。";
            return;
        }

        var currentCount = InventoryService.Count(watchedItem);
        itemBaseline = resultTarget.GetBaseline(preparedBaseline, currentCount);
        observedCraft = preparedCraftObserved || IsSynthesizing();
        craftClosed = null;
        observedMacro = false;
        macroName = macro->Name.ToString();
        started = DateTime.UtcNow;
        step = value;
        ownsMacro = true;
        Plugin.Log.Information($"Macro start: bank={(value.Shared ? "shared" : "individual")}, number={value.Number}, " +
            $"completion={value.Completion}, source={resultSource}, recipe={watchedRecipe}, resultItem={watchedItem}, " +
            $"baseline={itemBaseline}, current={currentCount}, requiredIncrease={requiredIncrease}, " +
            $"configuredItem={value.ItemId}, configuredMinimum={value.MinimumIncrease}, craftObserved={observedCraft}.");
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
            return Complete(0);
        }
        var currentCount = InventoryService.Count(watchedItem);
        var increase = currentCount - itemBaseline;
        if (step.Completion == CompletionKind.ItemIncreased)
        {
            if (increase < requiredIncrease) return WorkResult.Pending;
            if (crafting) return WorkResult.Pending;
            return Complete(currentCount);
        }
        if (crafting) return WorkResult.Pending;
        if (observedCraft && increase >= requiredIncrease)
        {
            return Complete(currentCount);
        }
        if (observedCraft)
        {
            craftClosed ??= DateTime.UtcNow;
            // Inventory updates can arrive shortly after the synthesis addon closes.
            if ((DateTime.UtcNow - craftClosed.Value).TotalSeconds > 4)
                return Fail($"制作结束但未检测到成品增加（物品 {watchedItem}：{itemBaseline} → {currentCount}），请检查本次制作结果。");
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
            // Capture the existing book and button state before opening or changing a recipe.
            ReportPrepareProgress(recipeId, "before preparation");
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
            ReportPrepareProgress(recipeId, "recipe validated");
        }
        if (recipeId != preparedRecipe) return Fail("运行中配方发生变化，请停止后重新开始。");
        if ((DateTime.UtcNow - prepareStarted).TotalSeconds > 20)
        {
            ReportPrepareProgress(recipeId, "timeout", true);
            return Fail($"打开配方或开始制作超时：{Progress}。请检查材料、职业、状态及制作条件。");
        }
        if (prepareStage == 3)
        {
            ReportPrepareProgress(recipeId, "await synthesis");
            if (!IsSynthesizing()) return WorkResult.Pending;
            var active = AgentRecipeNote.Instance();
            if (active != null && active->ActiveCraftRecipeId != 0 && active->ActiveCraftRecipeId != recipeId)
                return Fail("实际制作配方与配置不一致。");
            preparedCraftObserved = true;
            ReportPrepareProgress(recipeId, "synthesis observed", true);
            Progress = "本轮制作已启动，准备执行宏。";
            prepareStage = 0;
            return WorkResult.Complete;
        }
        if (DateTime.UtcNow < nextPrepareAction) return WorkResult.Pending;
        ReportPrepareProgress(recipeId, "await recipe readiness");
        var agent = AgentRecipeNote.Instance();
        if (agent == null) return WorkResult.Pending;
        var note = RecipeNote.Instance();
        var addon = (AddonRecipeNote*)Plugin.GameGui.GetAddonByName("RecipeNote", 1).Address;
        if (prepareStage == 1)
        {
            var currentSelection = note == null || note->RecipeList == null
                ? null : note->RecipeList->SelectedRecipe;
            if (CraftPreparationDecision.CanReuseSelectedRecipe(recipeId, addon != null && addon->IsVisible,
                note != null && note->IsRecipeListReady, currentSelection == null ? 0u : currentSelection->RecipeId))
            {
                // During IdleBetween, opening the same recipe again can close the book.
                // Reuse its current selection and still apply every synthesis guard below.
                prepareStage = 2;
                ReportPrepareProgress(recipeId, "reuse already selected recipe", true);
            }
            else
            {
                ReportPrepareProgress(recipeId, "before OpenRecipeByRecipeId", true);
                agent->OpenRecipeByRecipeId(recipeId);
                prepareStage = 2;
                nextPrepareAction = DateTime.UtcNow.AddMilliseconds(800);
                ReportPrepareProgress(recipeId, "after OpenRecipeByRecipeId", true);
                return WorkResult.Pending;
            }
        }
        if (addon == null || !addon->IsVisible || note == null || note->RecipeList == null)
            return WorkResult.Pending;
        var selected = note->RecipeList->SelectedRecipe;
        if (selected == null || selected->RecipeId != recipeId || !note->IsRecipeListReady)
            return WorkResult.Pending;
        if (addon->SynthesizeButton == null || !addon->SynthesizeButton->IsEnabled)
            return WorkResult.Pending;
        // Normal synthesis callback, checked against the installed CN API15 client and Artisan.
        ReportPrepareProgress(recipeId, "before normal synthesis callback 8", true);
        AtkValue callback = new() { Type = AtkValueType.Int, Int = 8 };
        addon->FireCallback(1, &callback, true);
        prepareStage = 3; // Never repeat this callback: a failed start should time out safely.
        ReportPrepareProgress(recipeId, "after normal synthesis callback 8", true);
        return WorkResult.Pending;
    }

    public void Reset()
    {
        CancelOwnedMacro();
        step = null;
        prepareStage = 0;
        preparedRecipe = preparedItem = 0;
        preparedCraftObserved = false;
        prepareStarted = default;
        lastPrepareSignature = "";
        Progress = "";
        Error = "";
    }

    private WorkResult Fail(string message)
    {
        if (step != null)
        {
            var currentCount = InventoryService.Count(watchedItem);
            Plugin.Log.Warning($"Macro failed: bank={(step.Shared ? "shared" : "individual")}, number={step.Number}, " +
                $"completion={step.Completion}, source={resultSource}, recipe={watchedRecipe}, resultItem={watchedItem}, " +
                $"baseline={itemBaseline}, current={currentCount}, delta={currentCount - itemBaseline}, " +
                $"requiredIncrease={requiredIncrease}, craftObserved={observedCraft}. {message}");
        }
        Error = message;
        CancelOwnedMacro();
        step = null;
        prepareStage = 0;
        return WorkResult.Failed;
    }

    private WorkResult Complete(int currentCount)
    {
        Plugin.Log.Information($"Macro completed: bank={(step!.Shared ? "shared" : "individual")}, number={step.Number}, " +
            $"completion={step.Completion}, source={resultSource}, recipe={watchedRecipe}, resultItem={watchedItem}, " +
            $"baseline={itemBaseline}, current={currentCount}, delta={currentCount - itemBaseline}, " +
            $"requiredIncrease={requiredIncrease}, craftObserved={observedCraft}.");
        step = null;
        return WorkResult.Complete;
    }

    private void ReportPrepareProgress(uint recipeId, string reason, bool force = false)
    {
        var agent = AgentRecipeNote.Instance();
        var note = RecipeNote.Instance();
        var addon = (AddonRecipeNote*)Plugin.GameGui.GetAddonByName("RecipeNote", 1).Address;
        var list = note == null ? null : note->RecipeList;
        var recipeCount = list == null ? 0 : list->RecipeCount;
        var selectedIndex = list == null ? -1 : list->SelectedIndex;
        uint selectedRecipeId = 0;
        // Read the selected row for diagnostics even while IsRecipeListReady is false.
        // The actual synthesis guards below remain unchanged.
        if (list != null && list->Recipes != null && recipeCount is > 0 and <= 65536
            && selectedIndex >= 0 && selectedIndex < recipeCount)
            selectedRecipeId = list->Recipes[selectedIndex].RecipeId;
        var visible = addon != null && addon->IsVisible;
        var ready = note != null && note->IsRecipeListReady;
        var hasButton = addon != null && addon->SynthesizeButton != null;
        var enabled = hasButton && addon->SynthesizeButton->IsEnabled;
        var synthVisible = IsSynthesizing();
        var activeRecipeId = agent == null ? 0 : agent->ActiveCraftRecipeId;
        var agentActive = agent != null && agent->IsAgentActive();
        var crafting = Plugin.Condition[ConditionFlag.Crafting];
        var executing = Plugin.Condition[ConditionFlag.ExecutingCraftingAction];
        var preparing = Plugin.Condition[ConditionFlag.PreparingToCraft];
        var occupied = Plugin.Condition[ConditionFlag.Occupied];
        var eventOccupied = Plugin.Condition[ConditionFlag.OccupiedInEvent];

        Progress = prepareStage switch
        {
            0 => "检查配方与当前制作状态",
            1 => agent == null ? "等待配方代理就绪" : "准备打开配方",
            3 => synthVisible ? "制作窗口已出现，正在核对配方" : "已请求开始制作，等待制作窗口",
            _ when agent == null => "等待配方代理就绪",
            _ when addon == null || !visible => "等待配方窗口显示",
            _ when list == null || !ready => "等待配方列表就绪",
            _ when selectedRecipeId != recipeId => $"等待选中配方 {recipeId}（当前 {selectedRecipeId}）",
            _ when !hasButton => "等待制作按钮出现",
            _ when !enabled => "等待制作按钮可用（请检查材料与当前制作状态）",
            _ => "配方与制作按钮已就绪，准备开始制作",
        };

        var signature = $"stage={prepareStage}, reason={reason}, recipe={recipeId}, agent={agent != null}, " +
            $"agentActive={agentActive}, agentActiveRecipe={activeRecipeId}, addon={addon != null}, visible={visible}, " +
            $"note={note != null}, list={list != null}, listReady={ready}, selectedRecipe={selectedRecipeId}, " +
            $"selectedIndex={selectedIndex}, recipeCount={recipeCount}, synthButton={hasButton}, buttonEnabled={enabled}, " +
            $"synthesisVisible={synthVisible}, crafting={crafting}, executingAction={executing}, preparingToCraft={preparing}, " +
            $"occupied={occupied}, occupiedInEvent={eventOccupied}";
        if (!force && signature == lastPrepareSignature) return;
        lastPrepareSignature = signature;
        var elapsed = prepareStarted == default ? 0 : (DateTime.UtcNow - prepareStarted).TotalSeconds;
        Plugin.Log.Information($"Craft preparation: {signature}, elapsedSeconds={elapsed:F2}. {Progress}");
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
