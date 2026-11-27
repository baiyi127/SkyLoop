// Only configuration hosting is replaced. All target and baseline rules under test
// are linked directly from the production MacroResultTarget implementation.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace SkyLoop
{
    public static class Plugin
    {
        public static ConfigurationHost PluginInterface { get; } = new();
    }

    public sealed class ConfigurationHost
    {
        public void SavePluginConfig(object configuration) { }
    }
}
