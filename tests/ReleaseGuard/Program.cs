using System.Reflection;
using Host = mom.PublicShell.Plugin;
using Dalamud.Interface.Windowing;

var checks = 0;
const string message = "You are not on Dalamud Release";
var prefix = "mom.Access.";
var commands = new[] { "/mom" };
void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; }
Host Create(State state)
{
    State.Current = state;
    foreach (var (property, value) in new (string, object)[] { ("PluginInterface", state.Interface), ("CommandManager", state.Commands), ("Log", state.Log), ("Textures", state.Textures) })
        typeof(Host).GetProperty(property)!.SetValue(null, value);
    return new Host();
}
mom.PublicShell.ModuleLoader Loader(Host host) => (mom.PublicShell.ModuleLoader)typeof(Host).GetField("loader", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;

var cases = new List<(string? Track, bool Allowed)>
{
    ("release", true), (" RELEASE ", true), ("ReLeAsE", true), ("stg", false), ("dev", false),
    ("release-candidate", false), ("preview", false), ("future", false), (null, false), ("", false), (" \t ", false),
};
for (var api = 16; api <= 20; api++) cases.Add(("api" + api, false));
cases.Add(("api99", false));
foreach (var (track, allowed) in cases)
{
    var state = new State { Track = track };
    var host = Create(state);
    Check(state.Reads == 1 && state.TrackReads == 1, "One detector and track read per instance: " + track);
    Check(state.Interface.Registered == 3 && state.Interface.UiBuilder.Count == 3 && state.Commands.Handlers.Count == commands.Length,
        "Handlers and IPC remain available: " + track);
    Check(state.Interface.GetIpcProvider<string>(prefix + "Directory.v1").Callback!() == Path.Combine("synthetic-config", "tasks"), "Directory IPC remains a lookup.");
    var window = state.Windows.Single();
    var validate = state.Interface.GetIpcProvider<byte[], bool>(prefix + "Validate.v1").Callback!;
    var refresh = state.Interface.GetIpcProvider<bool>(prefix + "Refresh.v1").Callback!;
    var retainedCommand = state.Commands.Handlers[commands[0]];
    if (allowed)
    {
        Check(state.Loads == 1 && state.PresentationCreates == 1 && state.IntroductionCreates == 1 && state.Errors == 0, "Release retains normal startup.");
        Check(validate([1]) && !validate([]) && state.PackageChecks == 2, "Release validation preserves success/false package semantics.");
        state.Interface.UiBuilder.DrawNow();
        state.Interface.UiBuilder.MainNow();
        state.Interface.UiBuilder.ConfigNow();
        foreach (var command in commands) state.Commands.Handlers[command](command, "status");
        Check(state.PrivateDraws == 1 && state.PrivateOpens >= 1 && state.CommandCalls.Count >= commands.Length, "Release callbacks forward.");
        Check(refresh() && state.Loads == 2, "Release refresh loads.");
        state.Track = "stg";
        Check(refresh() && state.Loads == 3 && state.Reads == 1, "Release decision remains cached until reload.");
    }
    else
    {
        Check(state.Loads == 0 && state.PresentationCreates == 0 && state.IntroductionCreates == 0, "Denied startup does not load private code or construct normal UI.");
        Check(window.IsOpen && state.Errors == 1, "Denied startup opens and logs once.");
        Check(state.Messages.Single().Contains(message, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(track) ? state.Messages.Single().Contains("<unknown>", StringComparison.Ordinal) : state.Messages.Single().Contains(track.Trim(), StringComparison.Ordinal)), "Startup log includes denial and track.");
        // A module supplied after denial must not create a callback bypass.
        Loader(host).Module = new mom.PublicShell.Module(state);
        for (var i = 0; i < 3; i++)
        {
            state.Interface.UiBuilder.DrawNow();
            Check(state.FrameText.SequenceEqual(new[] { message }), "Denied body contains only the exact message.");
            foreach (var command in commands)
            {
                window.IsOpen = false;
                state.Commands.Handlers[command](command, "start");
                Check(window.IsOpen, "Command reopens the same error window.");
            }
            window.IsOpen = false; state.Interface.UiBuilder.MainNow(); Check(window.IsOpen, "Open Main reopens error.");
            window.IsOpen = false; state.Interface.UiBuilder.ConfigNow(); Check(window.IsOpen && state.SettingsOpens == 0, "Open Config shows error without appearance controls.");
            window.IsOpen = false; Check(!refresh() && window.IsOpen, "Denied refresh returns false and reopens error.");
            try { validate(null!); throw new Exception("Denied validation accepted."); }
            catch (InvalidOperationException error) { Check(error.Message == message, "Validation exposes explicit branch error before processing bytes."); }
        }
        state.Track = "release";
        Check(!refresh() && state.Loads == 0 && state.PackageChecks == 0 && state.Reads == 1 && state.Errors == 1, "Changing reported track cannot bypass cached denial.");
        Check(state.PrivateDraws == 0 && state.PrivateOpens == 0 && state.CommandCalls.Count == 0, "Denied callbacks never forward private operations.");
        Check(ReferenceEquals(window, state.Windows.Single()), "All denial actions share one window.");
    }
    var loads = state.Loads;
    host.Dispose(); host.Dispose();
    retainedCommand(commands[0], "start"); state.Interface.UiBuilder.DrawNow();
    Check(!refresh() && state.Loads == loads, "Disposed callbacks cannot reload.");
    Check(state.LoaderDisposals == 1 && state.PresentationDisposals == (allowed ? 1 : 0), "Both states clean up exactly once.");
    Check(state.Windows.Count == 0 && state.Interface.Registered == 0 && state.Interface.UiBuilder.Count == 0 && state.Commands.Handlers.Count == 0, "Dispose removes windows/commands/events/providers.");
}
foreach (var fault in new[] { "detector", "track", "null info", "logger" })
{
    var state = new State { Track = "stg", ThrowDetection = fault == "detector", ThrowTrack = fault == "track", NullInfo = fault == "null info", ThrowLog = fault == "logger" };
    using var host = Create(state);
    state.Interface.UiBuilder.DrawNow();
    Check(state.Reads == 1 && state.Loads == 0 && state.Windows.Single().IsOpen && state.FrameText.SequenceEqual(new[] { message }), "Detection/logging fault retains error shell: " + fault);
    Check(state.Messages.Single().Contains(fault == "logger" ? "stg" : "Branch check failed", StringComparison.Ordinal), "Failure diagnostics: " + fault);
}
for (var api = 16; api <= 20; api++)
{
    var state = new State { ApiMajor = api, Track = "release" };
    using var host = Create(state);
    Check(state.Loads == 1 && state.Errors == 0 && state.Reads == 1, "Future API release track is eligible.");
}
foreach (var track in new[] { "release", "stg" })
foreach (var fault in new[] { "draw", "main", "config", "Directory", "Validate", "Refresh" })
{
    var state = new State { Track = track, Failure = fault };
    Exception? caught = null;
    try { Create(state); } catch (Exception error) { caught = error; }
    Check(ReferenceEquals(caught, state.Original) && state.Loads == 0 && state.LoaderDisposals == 1 && state.Windows.Count == 0 &&
        state.Interface.UiBuilder.Count == 0 && state.Interface.Registered == 0 && state.Commands.Handlers.Count == 0, "Construction rollback: " + track + "/" + fault);
}
var empty = new State { NoModule = true };
using (var host = Create(empty))
{
    empty.Commands.Handlers[commands[0]](commands[0], "status");
    empty.Interface.UiBuilder.DrawNow();
    Check(empty.Windows.Single().IsOpen && empty.FrameText.SequenceEqual(new[] { "Normal introduction" }), "Release without access retains introduction.");
    Check(!empty.Interface.GetIpcProvider<bool>(prefix + "Refresh.v1").Callback!(), "Release refresh without module returns false.");
}
var reload = new State { Track = "stg" };
using (Create(reload)) { }
reload.Track = "release";
using (Create(reload)) { Check(reload.Reads == 2 && reload.Loads == 1, "Reload creates exactly one fresh decision."); }
Console.WriteLine($"PASS: {checks} source-linked mom release-guard checks. Dalamud/UI/package/module services are synthetic; no private or game code runs.");
