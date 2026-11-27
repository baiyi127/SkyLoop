using System.Numerics;
using System.Text.Json;
using SkyLoop;
using SkyLoop.Services;

var checks = new (string Name, Action Run)[]
{
    ("saved approach point wins over the live NPC center", ApproachWinsOverLiveNpc),
    ("saved approach point works before the NPC is loaded", ApproachWithoutLoadedNpc),
    ("legacy NPC destinations retain live-position fallback", LegacyLiveNpcFallback),
    ("legacy NPC destinations retain saved-position fallback", LegacySavedPositionFallback),
    ("old configuration does not silently become a saved approach point", LegacyConfigurationDefaults),
    ("already at the user approach point sends no path or movement", AlreadyAtApproachPoint),
    ("approach arrival uses its tighter 0.6 meter radius", ApproachArrivalRadius),
    ("canceling pending pathfinding cannot start movement later", CancelPendingPath),
    ("stopping owned movement sends the navigation stop action", StopOwnedMovement),
    ("another plugin's running path is left untouched", PreserveOtherMovement),
    ("a path ownership race cannot replace another plugin's movement", PreserveMovementStartedDuringQuery),
    ("a canceled old query cannot replace a newer destination", OldQueryCannotReplaceNewTrip),
    ("non-finite saved approach coordinates cannot trigger a path", RejectInvalidApproach),
    ("a territory change cancels a pending query", TerritoryChangeCancelsQuery),
};
var passed = 0;
foreach (var check in checks)
{
    Reset();
    try { check.Run(); Console.WriteLine("PASS " + check.Name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{passed}/{checks.Length} navigation checks passed.");
return passed == checks.Length ? 0 : 1;

static Vector3 NpcCenter() => new(40.48f, -16f, 138.48f);
static Vector3 Approach() => new(40.92f, -16f, 145.30f);

static NpcLocation Location(bool approach = true) => new()
{
    Saved = true, Name = "exchange", TerritoryId = 886, DataId = 1234,
    X = 40.48f, Y = -16, Z = 138.48f,
    ApproachSaved = approach, ApproachX = 40.92f, ApproachY = -16, ApproachZ = 145.30f,
};

static void Reset()
{
    Plugin.PluginInterface.Reset();
    Plugin.ClientState.TerritoryType = 886;
    Plugin.ObjectTable.Clear();
    Plugin.ObjectTable.LocalPlayer = new TestGameObject { Position = new Vector3(20, -16, 145.30f) };
    Plugin.ObjectTable.Add(new TestGameObject { BaseId = 1234, Position = NpcCenter() });
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectVector(Vector3 actual, Vector3 expected, string message) =>
    Expect(Vector3.Distance(actual, expected) < 0.001f, $"{message}: expected {expected}, got {actual}");

static List<IpcCall> Calls(string name) => Plugin.PluginInterface.Calls.Where(x => x.Name == name).ToList();

static Vector3 RequestedDestination() =>
    (Vector3)Calls("vnavmesh.Nav.PathfindCancelable").Single().Arguments[1]!;

static void ApproachWinsOverLiveNpc()
{
    var navigation = new NavigationService(); navigation.Start(Location());
    Expect(navigation.Tick() == WorkResult.Pending, "pathfinding should be requested");
    ExpectVector(RequestedDestination(), Approach(), "the loaded NPC must not overwrite the user's stand position");
    navigation.Stop();
}

static void ApproachWithoutLoadedNpc()
{
    Plugin.ObjectTable.Clear();
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick();
    ExpectVector(RequestedDestination(), Approach(), "approach point must work without an object-table NPC");
    navigation.Stop();
}

static void LegacyLiveNpcFallback()
{
    var location = Location(false); location.X = 200; location.Z = 200;
    var navigation = new NavigationService(); navigation.Start(location); navigation.Tick();
    ExpectVector(RequestedDestination(), NpcCenter(), "locations without an approach point retain live NPC fallback");
    navigation.Stop();
}

static void LegacySavedPositionFallback()
{
    Plugin.ObjectTable.Clear();
    var navigation = new NavigationService(); navigation.Start(Location(false)); navigation.Tick();
    ExpectVector(RequestedDestination(), NpcCenter(), "unloaded legacy NPC retains its saved identity anchor");
    navigation.Stop();
}

static void LegacyConfigurationDefaults()
{
    const string oldLocation = """{"Saved":true,"TerritoryId":886,"DataId":1234,"X":40.48,"Y":-16,"Z":138.48}""";
    var location = JsonSerializer.Deserialize<NpcLocation>(oldLocation, new JsonSerializerOptions { IncludeFields = true })!;
    Expect(!location.ApproachSaved, "old NPC-center coordinates must not be silently reinterpreted as a user stand position");
    ExpectVector(location.Position, NpcCenter(), "legacy identity anchor must survive configuration loading");
    var navigation = new NavigationService(); navigation.Start(location); navigation.Tick();
    ExpectVector(RequestedDestination(), NpcCenter(), "legacy navigation behavior must remain available");
    navigation.Stop();
}

static void AlreadyAtApproachPoint()
{
    Plugin.ObjectTable.LocalPlayer!.Position = Approach();
    Expect(Vector3.Distance(Approach(), NpcCenter()) > 6, "fixture must retain the user's farther interaction stand position");
    var navigation = new NavigationService(); navigation.Start(Location());
    Expect(navigation.Tick() == WorkResult.Complete, "already standing at the approach point must complete immediately");
    Expect(Calls("vnavmesh.Nav.PathfindCancelable").Count == 0 && Calls("vnavmesh.Path.MoveTo").Count == 0,
        "do not walk around the counter toward the NPC center");
}

static void ApproachArrivalRadius()
{
    Plugin.ObjectTable.LocalPlayer!.Position = Approach() + new Vector3(0.59f, 0, 0);
    var navigation = new NavigationService(); navigation.Start(Location());
    Expect(navigation.Tick() == WorkResult.Complete, "0.59 meters is within the explicit stand-position radius");
    Reset();
    Plugin.ObjectTable.LocalPlayer!.Position = Approach() + new Vector3(0.61f, 0, 0);
    navigation = new NavigationService(); navigation.Start(Location());
    Expect(navigation.Tick() == WorkResult.Pending, "0.61 meters must not reuse the legacy 2.8 meter NPC radius");
    Expect(Calls("vnavmesh.Nav.PathfindCancelable").Count == 1, "approach point needs its own arrival threshold");
    navigation.Stop();
}

static void CancelPendingPath()
{
    var pending = new TaskCompletionSource<List<Vector3>>();
    CancellationToken cancellation = default;
    Plugin.PluginInterface.PathFactory = (from, to, token) => { cancellation = token; return pending.Task; };
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick();
    navigation.Stop();
    Expect(cancellation.IsCancellationRequested, "stop must cancel the outstanding query token");
    pending.SetResult([Approach()]); // Simulate a backend that finishes despite cancellation.
    Expect(navigation.Tick() == WorkResult.Complete, "stopped query must remain inactive");
    Expect(Calls("vnavmesh.Path.MoveTo").Count == 0, "late path completion must never start movement");
}

static void StopOwnedMovement()
{
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick(); navigation.Tick();
    Expect(Calls("vnavmesh.Path.MoveTo").Count == 1 && Plugin.PluginInterface.PathRunning, "fixture must start owned movement");
    navigation.Stop();
    Expect(Calls("vnavmesh.Path.Stop").Count == 1 && !Plugin.PluginInterface.PathRunning,
        "stop must terminate the path that this plugin started");
}

static void PreserveOtherMovement()
{
    Plugin.PluginInterface.PathRunning = true;
    var navigation = new NavigationService(); navigation.Start(Location());
    Expect(navigation.Tick() == WorkResult.Failed, "another running path must prevent this path from starting");
    Expect(Calls("vnavmesh.Path.Stop").Count == 0 && Plugin.PluginInterface.PathRunning,
        "failure before acquiring movement must not stop the other plugin's path");
}

static void PreserveMovementStartedDuringQuery()
{
    var pending = new TaskCompletionSource<List<Vector3>>();
    Plugin.PluginInterface.PathFactory = (from, to, token) => pending.Task;
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick();
    Plugin.PluginInterface.PathRunning = true;
    pending.SetResult([Approach()]);
    Expect(navigation.Tick() == WorkResult.Failed, "movement ownership must be rechecked after asynchronous pathfinding");
    Expect(Calls("vnavmesh.Path.MoveTo").Count == 0 && Calls("vnavmesh.Path.Stop").Count == 0,
        "completed query must neither replace nor cancel the newly started external path");
}

static void OldQueryCannotReplaceNewTrip()
{
    var oldQuery = new TaskCompletionSource<List<Vector3>>();
    var newQuery = new TaskCompletionSource<List<Vector3>>();
    Plugin.PluginInterface.PathFactory = (from, to, token) => oldQuery.Task;
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick(); navigation.Stop();
    var next = Location(); next.ApproachX = 50;
    Plugin.PluginInterface.PathFactory = (from, to, token) => newQuery.Task;
    navigation.Start(next); navigation.Tick();
    oldQuery.SetResult([Approach()]);
    Expect(navigation.Tick() == WorkResult.Pending && Calls("vnavmesh.Path.MoveTo").Count == 0,
        "old query completion must not displace the newer pending trip");
    newQuery.SetResult([next.ApproachPosition]); navigation.Tick();
    var moved = (List<Vector3>)Calls("vnavmesh.Path.MoveTo").Single().Arguments[0]!;
    ExpectVector(moved.Single(), next.ApproachPosition, "only the new destination may start movement");
    navigation.Stop();
}

static void RejectInvalidApproach()
{
    var location = Location(); location.ApproachX = float.NaN;
    var navigation = new NavigationService(); navigation.Start(location);
    Expect(navigation.Tick() == WorkResult.Failed, "non-finite approach coordinates must be rejected before pathfinding");
    Expect(Calls("vnavmesh.Nav.PathfindCancelable").Count == 0, "invalid coordinates must never reach IPC");
}

static void TerritoryChangeCancelsQuery()
{
    var pending = new TaskCompletionSource<List<Vector3>>();
    CancellationToken cancellation = default;
    Plugin.PluginInterface.PathFactory = (from, to, token) => { cancellation = token; return pending.Task; };
    var navigation = new NavigationService(); navigation.Start(Location()); navigation.Tick();
    Plugin.ClientState.TerritoryType = 999;
    Expect(navigation.Tick() == WorkResult.Failed && cancellation.IsCancellationRequested,
        "changing territory must cancel the pending route");
    pending.SetResult([Approach()]); navigation.Tick();
    Expect(Calls("vnavmesh.Path.MoveTo").Count == 0, "canceled cross-territory route cannot start later");
}
