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
    private readonly ModuleLoader loader = new();
    private readonly IntroductionWindow introduction;
    private readonly List<Action> cleanup = [];
    private int disposed;
    private bool IsDisposed => System.Threading.Volatile.Read(ref disposed) != 0;

    public Plugin()
    {
        introduction = new IntroductionWindow(PluginInterface, Textures, loader, RefreshAccess);
        try
        {
            cleanup.Add(windows.RemoveAllWindows);
            windows.AddWindow(introduction);
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
            refresh.RegisterFunc(() => { RefreshAccess(); return loader.Module != null && !loader.Failed; });
            loader.Load(PluginInterface);
        }
        catch { Dispose(); throw; }
    }

    private static bool ValidateAccess(byte[] bytes)
    {
        try
        {
            var plaintext = ModulePackage.VerifyAndDecrypt(bytes, TrustAnchor.PublicKey, Version.Parse(BuildInfo.Version));
            CryptographicOperations.ZeroMemory(plaintext);
            return true;
        }
        catch { return false; }
    }
    private void RefreshAccess() { if (IsDisposed) return; loader.Load(PluginInterface); if (loader.Module is { } module) { introduction.IsOpen = false; module.OpenMainWindow(); } }
    private void Open() { if (IsDisposed) return; if (loader.Module is { } module) module.OpenMainWindow(); else introduction.IsOpen = true; }
    private void OpenConfig() { if (IsDisposed) return; if (loader.Module is { } module) module.OnCommand("/mom", "config"); else introduction.IsOpen = true; }
    private void OnCommand(string command, string arguments) { if (IsDisposed) return; if (loader.Module is { } module) module.OnCommand(command, arguments); else introduction.IsOpen = true; }
    private void Draw() { if (IsDisposed) return; windows.Draw(); loader.Module?.Draw(); }
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref disposed, 1) != 0) return;
        for (var index = cleanup.Count - 1; index >= 0; --index) Cleanup(cleanup[index]);
        cleanup.Clear();
        Cleanup(loader.Dispose);
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
