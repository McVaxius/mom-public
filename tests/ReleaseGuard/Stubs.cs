using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

public sealed class State
{
    public static State Current = null!;
    public string? Track = "release";
    public int ApiMajor = 15;
    public bool ThrowDetection, ThrowTrack, NullInfo, ThrowLog, NoModule;
    public string Failure = "";
    public int Reads, TrackReads, Loads, PackageChecks, LoaderDisposals, PresentationCreates, PresentationDisposals, IntroductionCreates;
    public int PrivateDraws, PrivateOpens, SettingsOpens, WindowDraws, Errors, Warnings;
    public readonly List<string> CommandCalls = [], Messages = [], FrameText = [];
    public readonly List<Dalamud.Interface.Windowing.Window> Windows = [];
    public readonly Exception Original = new InvalidOperationException("registration fault");
    public FakeInterface Interface { get; }
    public FakeCommands Commands { get; }
    public FakeLog Log { get; }
    public FakeTextures Textures { get; } = new();
    public State() { Interface = new(this); Commands = new(); Log = new(this); }
    public void Register(string label) { if (Failure == label) throw Original; }
}

public sealed class FakeInterface(State state) : IDalamudPluginInterface
{
    private readonly Dictionary<string, object> providers = [];
    public FakeUi UiBuilder { get; } = new(state);
    public Dalamud.Plugin.VersionInfo.IDalamudVersionInfo GetDalamudVersion()
    {
        state.Reads++;
        if (state.ThrowDetection) throw new InvalidOperationException("detector failed");
        return state.NullInfo ? null! : new FakeVersion(state);
    }
    public string GetPluginConfigDirectory() => "synthetic-config";
    public Provider<T> GetIpcProvider<T>(string name)
    {
        if (!providers.TryGetValue(name, out var value)) providers[name] = value = new Provider<T>(state, name);
        return (Provider<T>)value;
    }
    public Provider<TArg, T> GetIpcProvider<TArg, T>(string name)
    {
        if (!providers.TryGetValue(name, out var value)) providers[name] = value = new Provider<TArg, T>(state, name);
        return (Provider<TArg, T>)value;
    }
    public int Registered => providers.Values.Count(value => ((IProvider)value).Registered);
}
public sealed class FakeVersion(State state) : Dalamud.Plugin.VersionInfo.IDalamudVersionInfo
{
    public Version Version => new(state.ApiMajor, 0, 3, 6);
    public string? BetaTrack { get { state.TrackReads++; if (state.ThrowTrack) throw new InvalidOperationException("track failed"); return state.Track; } }
    public string? GitHashClientStructs => "synthetic-cs-hash";
    public string? GitHash => "synthetic-hash";
    public string? ScmVersion => null;
}
public interface IProvider { bool Registered { get; } }
public sealed class Provider<T>(State state, string name) : IProvider
{
    public Func<T>? Callback;
    public bool Registered { get; private set; }
    public void RegisterFunc(Func<T> callback) { Callback = callback; Registered = true; state.Register(name.Split('.')[2]); }
    public void UnregisterFunc() { Callback = null; Registered = false; }
}
public sealed class Provider<TArg, T>(State state, string name) : IProvider
{
    public Func<TArg, T>? Callback;
    public bool Registered { get; private set; }
    public void RegisterFunc(Func<TArg, T> callback) { Callback = callback; Registered = true; state.Register(name.Split('.')[2]); }
    public void UnregisterFunc() { Callback = null; Registered = false; }
}
public sealed class FakeUi(State state)
{
    private Action? draw, main, config;
    public event Action Draw { add { draw += value; state.Register("draw"); } remove { draw -= value; } }
    public event Action OpenMainUi { add { main += value; state.Register("main"); } remove { main -= value; } }
    public event Action OpenConfigUi { add { config += value; state.Register("config"); } remove { config -= value; } }
    public int Count => (draw?.GetInvocationList().Length ?? 0) + (main?.GetInvocationList().Length ?? 0) + (config?.GetInvocationList().Length ?? 0);
    public void DrawNow() { state.FrameText.Clear(); draw?.Invoke(); }
    public void MainNow() => main?.Invoke();
    public void ConfigNow() => config?.Invoke();
}
public sealed class FakeCommands : ICommandManager
{
    public readonly Dictionary<string, Action<string, string>> Handlers = [];
    public bool AddHandler(string name, CommandInfo info) => Handlers.TryAdd(name, info.Handler);
    public void RemoveHandler(string name) => Handlers.Remove(name);
}
public sealed class FakeLog(State state) : IPluginLog
{
    public void Error(string message, params object[] args) { state.Errors++; state.Messages.Add(message + " " + string.Join(" ", args)); if (state.ThrowLog) throw new Exception("logger failed"); }
    public void Error(Exception error, string message, params object[] args) => Error(message, args);
    public void Warning(Exception error, string message) => state.Warnings++;
}
public sealed class FakeTextures : ITextureProvider { }

namespace Dalamud.Game.Command
{
    public sealed class CommandInfo(Action<string, string> handler)
    {
        public Action<string, string> Handler = handler;
        public string HelpMessage { get; set; } = "";
    }
}
namespace Dalamud.IoC { [AttributeUsage(AttributeTargets.Property)] public sealed class PluginServiceAttribute : Attribute { } }
namespace Dalamud.Plugin.Services
{
    public interface ICommandManager { bool AddHandler(string name, CommandInfo info); void RemoveHandler(string name); }
    public interface IPluginLog { void Error(string message, params object[] args); void Error(Exception error, string message, params object[] args); void Warning(Exception error, string message); }
    public interface ITextureProvider { }
}
namespace Dalamud.Plugin
{
    public interface IDalamudPlugin : IDisposable { }
    public interface IDalamudPluginInterface
    {
        FakeUi UiBuilder { get; }
        Dalamud.Plugin.VersionInfo.IDalamudVersionInfo GetDalamudVersion();
        string GetPluginConfigDirectory();
        Provider<T> GetIpcProvider<T>(string name);
        Provider<TArg, T> GetIpcProvider<TArg, T>(string name);
    }
}
namespace Dalamud.Plugin.VersionInfo
{
    public interface IDalamudVersionInfo { Version Version { get; } string? BetaTrack { get; } string? GitHashClientStructs { get; } string? GitHash { get; } string? ScmVersion { get; } }
}
namespace Dalamud.Bindings.ImGui
{
    public enum ImGuiCond { Appearing }
    public static class ImGui { public static void TextUnformatted(string text) => State.Current.FrameText.Add(text); }
}
namespace Dalamud.Interface.Windowing
{
    public abstract class Window(string name)
    {
        public string WindowName { get; } = name;
        public bool IsOpen { get; set; }
        public Vector2? Size { get; set; }
        public Dalamud.Bindings.ImGui.ImGuiCond SizeCondition { get; set; }
        public abstract void Draw();
    }
    public sealed class WindowSystem(string name)
    {
        public string Name { get; } = name;
        private readonly State state = State.Current;
        public void AddWindow(Window window) => state.Windows.Add(window);
        public void RemoveAllWindows() => state.Windows.Clear();
        public void Draw() { state.WindowDraws++; foreach (var window in state.Windows.Where(w => w.IsOpen)) window.Draw(); }
    }
}
namespace mom.PublicShell
{
    internal sealed class ModuleLoader : IDisposable
    {
        private readonly State state = State.Current;
        public Module? Module { get; set; }
        public bool Failed { get; set; }
        public void Load(IDalamudPluginInterface pi)
        {
            state.Loads++;
            if (pi is FakeInterface fake && fake.Registered != 3) throw new Exception("IPC must be registered before loading.");
            state.Register("load");
            Module = state.NoModule ? null : new Module(state);
        }
        public void Dispose() { state.LoaderDisposals++; Module = null; }
    }
    internal sealed class Module(State state)
    {
        public void Draw() => state.PrivateDraws++;
        public void OpenMainWindow() => state.PrivateOpens++;
        public void OnCommand(string command, string args) => state.CommandCalls.Add(command + " " + args);
    }
    internal static class ModulePackage
    {
        public static byte[] VerifyAndDecrypt(byte[] bytes, string key, Version version)
        {
            State.Current.PackageChecks++;
            if (bytes.Length == 0) throw new InvalidDataException("invalid package");
            return [1, 2, 3];
        }
    }
    internal static class TrustAnchor { public const string PublicKey = "fixture-key"; }
    internal static class BuildInfo { public const string Version = "1.0.0.0"; }
    internal sealed class IntroductionWindow : Dalamud.Interface.Windowing.Window
    {
        public IntroductionWindow(IDalamudPluginInterface pi, ITextureProvider textures, ModuleLoader loader, Action refresh, object presentation) : base("NormalIntroduction") => State.Current.IntroductionCreates++;
        public void OpenSettings() { State.Current.SettingsOpens++; IsOpen = true; }
        public override void Draw() => State.Current.FrameText.Add("Normal introduction");
    }
    internal sealed class PublicUi : IDisposable
    {
        public PublicUi(IDalamudPluginInterface pi, ITextureProvider textures) => State.Current.PresentationCreates++;
        public void Draw(Action draw, Action<Exception> error) => draw();
        public void Dispose() => State.Current.PresentationDisposals++;
    }
    internal static class AppearancePreferences { public static void Initialize(Func<object> load, Action<object> save) { } }
}
namespace mom.PublicShell.Ui
{
    internal sealed class PreferencePersistence(IDalamudPluginInterface pi) { public object Load() => pi.GetPluginConfigDirectory(); public void Save(object value) { } }
    internal sealed class PublicAppearance : IDisposable
    {
        public PublicAppearance(IDalamudPluginInterface pi, ITextureProvider textures) => State.Current.PresentationCreates++;
        public void Draw(Dalamud.Interface.Windowing.WindowSystem windows) => windows.Draw();
        public void Dispose() => State.Current.PresentationDisposals++;
    }
}
