using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Security.Cryptography;

namespace mom.PublicShell;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ITextureProvider Textures { get; private set; } = null!;
    private readonly WindowSystem windows = new("mom.Information");
    private const string ReleaseRequiredMessage = "You are not on Dalamud Release";
    private readonly ModuleLoader loader = new();
    private readonly ReleaseDecision release;
    private readonly Window publicWindow;
    private readonly PublicUi? presentation;
    private readonly IntroductionWindow? introduction;
    private readonly List<Action> cleanup = [];
    private int disposed;
    private bool IsDisposed => System.Threading.Volatile.Read(ref disposed) != 0;

    public Plugin()
    {
        release = ReleaseDecision.Capture(PluginInterface);
        try
        {
            if (release.Allowed)
            {
                presentation = new PublicUi(PluginInterface, Textures);
                introduction = new IntroductionWindow(PluginInterface, Textures, loader, RefreshAccess, presentation);
                publicWindow = introduction;
            }
            else publicWindow = new ReleaseRequiredWindow();
            cleanup.Add(windows.RemoveAllWindows);
            windows.AddWindow(publicWindow);
            if (!CommandManager.AddHandler("/mom", new CommandInfo(OnCommand) { HelpMessage = "Open MOM. Access modules provide additional commands." }))
                throw new InvalidOperationException("The /mom command is already registered.");
            cleanup.Add(() => CommandManager.RemoveHandler("/mom"));
            cleanup.Add(() => PluginInterface.UiBuilder.Draw -= Draw);
            PluginInterface.UiBuilder.Draw += Draw;
            cleanup.Add(() => PluginInterface.UiBuilder.OpenMainUi -= Open);
            PluginInterface.UiBuilder.OpenMainUi += Open;
            cleanup.Add(() => PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig);
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
            var directory = PluginInterface.GetIpcProvider<string>("mom.Access.Directory.v1");
            cleanup.Add(directory.UnregisterFunc);
            directory.RegisterFunc(() => Path.Combine(PluginInterface.GetPluginConfigDirectory(), "tasks"));
            var validate = PluginInterface.GetIpcProvider<byte[], bool>("mom.Access.Validate.v1");
            cleanup.Add(validate.UnregisterFunc);
            validate.RegisterFunc(ValidateAccess);
            var refresh = PluginInterface.GetIpcProvider<bool>("mom.Access.Refresh.v1");
            cleanup.Add(refresh.UnregisterFunc);
            refresh.RegisterFunc(() => { RefreshAccess(); return release.Allowed && loader.Module != null && !loader.Failed; });
            if (!release.Allowed)
            {
                ReportReleaseDenial();
                publicWindow.IsOpen = true;
                return;
            }
            loader.Load(PluginInterface);
        }
        catch { Dispose(); throw; }
    }

    private bool ValidateAccess(byte[] bytes)
    {
        // Keep branch rejection outside the package-error catch so APM receives the explicit reason.
        if (!release.Allowed) throw new InvalidOperationException(ReleaseRequiredMessage);
        try
        {
            var plaintext = ModulePackage.VerifyAndDecrypt(bytes, TrustAnchor.PublicKey, Version.Parse(BuildInfo.Version));
            CryptographicOperations.ZeroMemory(plaintext);
            return true;
        }
        catch { return false; }
    }
    private void RefreshAccess()
    {
        if (IsDisposed) return;
        if (!release.Allowed) { publicWindow.IsOpen = true; return; }
        loader.Load(PluginInterface);
        if (release.Allowed && loader.Module is { } module) { publicWindow.IsOpen = false; module.OpenMainWindow(); }
    }
    private void Open() { if (IsDisposed) return; if (release.Allowed && loader.Module is { } module) module.OpenMainWindow(); else publicWindow.IsOpen = true; }
    private void OpenConfig() { if (IsDisposed) return; if (release.Allowed && loader.Module is { } module) module.OnCommand("/mom", "config"); else publicWindow.IsOpen = true; }
    private void OnCommand(string command, string arguments) { if (IsDisposed) return; if (release.Allowed && loader.Module is { } module) module.OnCommand(command, arguments); else publicWindow.IsOpen = true; }
    private void Draw()
    {
        if (IsDisposed) return;
        if (!release.Allowed) { windows.Draw(); return; }
        if (publicWindow.IsOpen) presentation!.Draw(windows.Draw, error => Log.Error(error, "[mom] Public UI font coverage failed."));
        loader.Module?.Draw();
    }
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref disposed, 1) != 0) return;
        for (var index = cleanup.Count - 1; index >= 0; --index) Cleanup(cleanup[index]);
        cleanup.Clear();
        Cleanup(loader.Dispose);
        if (presentation != null) Cleanup(presentation.Dispose);
    }

    private void ReportReleaseDenial()
    {
        try { Log?.Error("[Access] {Reason}. Track: {Track}. {Detail}", ReleaseRequiredMessage, release.Track, release.Detail); }
        catch { /* A logging failure must not prevent the error shell from opening. */ }
    }

    private sealed record ReleaseDecision(bool Allowed, string Track, string Detail)
    {
        internal static ReleaseDecision Capture(IDalamudPluginInterface pluginInterface)
        {
#if LOCAL_DEV_BUILD
            // Private developer hosts also link this source; their runtime policy is separately scoped.
            return new ReleaseDecision(true, "<local-development>", "Public release policy is not applied to local development builds.");
#else
            try
            {
                var info = pluginInterface.GetDalamudVersion();
                var track = info.BetaTrack?.Trim();
                var allowed = string.Equals(track, "release", StringComparison.OrdinalIgnoreCase);
                var detail = $"Dalamud version: {info.Version}; ClientStructs Git hash: {info.GitHashClientStructs ?? "<unknown>"}.";
                if (string.IsNullOrWhiteSpace(track))
                    detail = "The runtime branch was not reported; release could not be confirmed. " + detail;
                return new ReleaseDecision(allowed, string.IsNullOrWhiteSpace(track) ? "<unknown>" : track, detail);
            }
            catch (Exception error)
            {
                return new ReleaseDecision(false, "<unknown>", $"Branch check failed: {error.GetType().Name}: {error.Message}");
            }
#endif
        }
    }

    private sealed class ReleaseRequiredWindow : Window
    {
        public ReleaseRequiredWindow() : base("MOM##DalamudReleaseRequired")
        {
            Size = new System.Numerics.Vector2(420, 100);
            SizeCondition = Dalamud.Bindings.ImGui.ImGuiCond.Appearing;
        }

        public override void Draw() => Dalamud.Bindings.ImGui.ImGui.TextUnformatted(ReleaseRequiredMessage);
    }

    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            try { Log?.Warning(error, "[mom] Public host cleanup failed."); }
            catch { /* Logging cannot interrupt cleanup or hide the construction error. */ }
        }
    }
}
