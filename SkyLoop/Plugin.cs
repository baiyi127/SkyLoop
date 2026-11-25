using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SkyLoop.Services;
using SkyLoop.Windows;

namespace SkyLoop;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    public Configuration Configuration { get; }
    public LoopController Controller { get; }
    private readonly WindowSystem windows = new("SkyLoop");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        RewardCatalog.Load();
        RewardCatalog.ApplyNpcDefaults(Configuration);
        Controller = new LoopController(Configuration);
        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
        Framework.Update += OnUpdate;
        CommandManager.AddHandler("/skyloop", new CommandInfo(OnCommand) { HelpMessage = "打开天穹循环；/skyloop start 开始；/skyloop stop 停止" });
        Log.Information("SkyLoop loaded; automation is stopped until Start is pressed.");
    }

    private void OnUpdate(IFramework framework) => Controller.Update();
    private void Toggle() => mainWindow.Toggle();
    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "start": Controller.Start(); break;
            case "stop": Controller.Stop(); break;
            default: Toggle(); break;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        Controller.Stop();
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        windows.RemoveAllWindows();
        CommandManager.RemoveHandler("/skyloop");
        Configuration.Save();
    }
}
