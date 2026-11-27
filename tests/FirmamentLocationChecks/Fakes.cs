namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace SkyLoop
{
    public static class Plugin
    {
        public static FakePluginInterface PluginInterface { get; } = new();
    }

    public sealed class FakePluginInterface
    {
        public void SavePluginConfig(object configuration) { }
    }
}
