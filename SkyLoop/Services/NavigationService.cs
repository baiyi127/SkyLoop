using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SkyLoop.Services;

/// <summary>Uses vnavmesh IPC only. The cancellable query never starts movement on its own.</summary>
public sealed class NavigationService
{
    private NpcLocation? target;
    private Vector3 destination;
    private CancellationTokenSource? cancellation;
    private Task<List<Vector3>>? path;
    private bool ownsMovement;
    private bool requested;
    private DateTime started;
    private DateTime lastProgress;
    private DateTime movementStarted;
    private Vector3 lastPosition;

    public string Error { get; private set; } = "";
    public bool IsAvailable
    {
        get
        {
            try { return Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc(); }
            catch { return false; }
        }
    }

    public void Start(NpcLocation location)
    {
        Stop();
        Error = "";
        if (!location.Saved)
        {
            Error = "尚未保存导航位置。";
            return;
        }
        if (location.TerritoryId != Plugin.ClientState.TerritoryType)
        {
            Error = "目标不在当前区域；请先进入保存位置所在区域。";
            return;
        }
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            Error = "角色尚未就绪。";
            return;
        }
        if (!float.IsFinite(location.X) || !float.IsFinite(location.Y) || !float.IsFinite(location.Z))
        {
            Error = "目标坐标无效。";
            return;
        }
        target = location;
        destination = location.Position;
        // BaseId remains stable across object respawns; object IDs do not.
        var npc = location.DataId == 0 ? null : Plugin.ObjectTable.FirstOrDefault(o => o.BaseId == location.DataId);
        if (npc != null) destination = npc.Position;
        started = lastProgress = DateTime.UtcNow;
        lastPosition = player.Position;
    }

    public WorkResult Tick()
    {
        if (Error.Length > 0) return WorkResult.Failed;
        if (target == null) return WorkResult.Complete;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || Plugin.ClientState.TerritoryType != target.TerritoryId)
            return Fail("角色或区域发生变化，导航停止。");
        if ((DateTime.UtcNow - started).TotalSeconds > 120)
            return Fail("导航超过 120 秒，已停止。");
        var tolerance = target.DataId == 0 ? 1.2f : 2.8f;
        if (Vector3.Distance(player.Position, destination) <= tolerance)
        {
            Stop();
            return WorkResult.Complete;
        }
        try
        {
            if (!requested)
            {
                if (!IsAvailable)
                {
                    if ((DateTime.UtcNow - started).TotalSeconds > 30)
                        return Fail("vnavmesh 未安装、未启用或导航网格尚未就绪。");
                    return WorkResult.Pending;
                }
                if (Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc()
                    || Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc())
                    return Fail("vnavmesh 正被其他导航任务使用，请先结束其他任务。");
                cancellation = new CancellationTokenSource();
                path = Plugin.PluginInterface
                    .GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
                    .InvokeFunc(player.Position, destination, false, cancellation.Token);
                requested = true;
                return WorkResult.Pending;
            }
            if (path != null)
            {
                if (!path.IsCompleted) return WorkResult.Pending;
                if (path.IsCanceled) return Fail("寻路已取消。");
                if (path.IsFaulted) return Fail("寻路失败：" + path.Exception?.GetBaseException().Message);
                var waypoints = path.GetAwaiter().GetResult();
                path = null;
                if (waypoints.Count == 0) return Fail("vnavmesh 未返回可行路径。");
                // Recheck ownership after async pathfinding before starting actual movement.
                if (Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc()
                    || Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc())
                    return Fail("寻路期间出现其他移动任务，已停止。");
                Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo").InvokeAction(waypoints, false);
                ownsMovement = true;
                movementStarted = lastProgress = DateTime.UtcNow;
                lastPosition = player.Position;
                return WorkResult.Pending;
            }
            if (Vector3.DistanceSquared(player.Position, lastPosition) >= 0.16f)
            {
                lastPosition = player.Position;
                lastProgress = DateTime.UtcNow;
            }
            if ((DateTime.UtcNow - lastProgress).TotalSeconds > 12)
                return Fail("连续 12 秒没有移动，可能被障碍物阻挡。");
            if ((DateTime.UtcNow - movementStarted).TotalSeconds > 1
                && !Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc())
                return Fail("移动已中断，但尚未到达目标。");
            return WorkResult.Pending;
        }
        catch (Exception exception)
        {
            return Fail("导航接口不可用：" + exception.Message);
        }
    }

    public void Stop()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        // Observe a late task failure without scheduling any movement after stop.
        if (path != null)
            _ = path.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        path = null;
        if (ownsMovement)
        {
            try { Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction(); }
            catch { /* vnavmesh can unload independently. */ }
        }
        ownsMovement = requested = false;
        target = null;
    }

    private WorkResult Fail(string message)
    {
        Stop();
        Error = message;
        return WorkResult.Failed;
    }
}
