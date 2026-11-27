using System.Numerics;

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace SkyLoop
{
    // Replace the external client and IPC endpoints, while compiling the actual
    // NavigationService and configuration unchanged into this test executable.
    public static class Plugin
    {
        public static TestObjectTable ObjectTable { get; } = new();
        public static TestClientState ClientState { get; } = new();
        public static TestPluginInterface PluginInterface { get; } = new();
        public static TestLog Log { get; } = new();
    }

    public class TestGameObject
    {
        public uint BaseId { get; set; }
        public Vector3 Position { get; set; }
    }

    public sealed class TestObjectTable : List<TestGameObject>
    {
        public TestGameObject? LocalPlayer { get; set; }
    }

    public sealed class TestClientState { public uint TerritoryType { get; set; } = 886; }
    public sealed class TestLog
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(Exception exception, string message) { }
    }

    public sealed record IpcCall(string Name, object?[] Arguments);

    public sealed class TestPluginInterface
    {
        public List<IpcCall> Calls { get; } = [];
        public bool Ready = true;
        public bool PathRunning;
        public bool OtherPathfinding;
        public Func<Vector3, Vector3, CancellationToken, Task<List<Vector3>>> PathFactory { get; set; } =
            (from, to, cancellation) => Task.FromResult(new List<Vector3> { from, to });

        public TestIpc<TResult> GetIpcSubscriber<TResult>(string name) => new(this, name);
        public TestIpc<TResult> GetIpcSubscriber<T1, T2, TResult>(string name) => new(this, name);
        public TestIpc<TResult> GetIpcSubscriber<T1, T2, T3, T4, TResult>(string name) => new(this, name);
        public void SavePluginConfig(object configuration) { }

        internal object? Invoke(string name, object?[] arguments)
        {
            Calls.Add(new IpcCall(name, arguments));
            switch (name)
            {
                case "vnavmesh.Nav.IsReady": return Ready;
                case "vnavmesh.Path.IsRunning": return PathRunning;
                case "vnavmesh.SimpleMove.PathfindInProgress": return OtherPathfinding;
                case "vnavmesh.Nav.PathfindCancelable":
                    return PathFactory((Vector3)arguments[0]!, (Vector3)arguments[1]!, (CancellationToken)arguments[3]!);
                case "vnavmesh.Path.MoveTo": PathRunning = true; return null;
                case "vnavmesh.Path.Stop": PathRunning = false; return null;
                default: throw new InvalidOperationException("Unexpected IPC endpoint: " + name);
            }
        }

        public void Reset()
        {
            Calls.Clear();
            Ready = true;
            PathRunning = OtherPathfinding = false;
            PathFactory = (from, to, cancellation) => Task.FromResult(new List<Vector3> { from, to });
        }
    }

    public sealed class TestIpc<TResult>(TestPluginInterface owner, string name)
    {
        public TResult InvokeFunc(params object?[] arguments) => (TResult)owner.Invoke(name, arguments)!;
        public void InvokeAction(params object?[] arguments) => owner.Invoke(name, arguments);
    }
}
