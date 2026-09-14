using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace mom.PublicShell;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windows = new("mom.PublicShell");
    private readonly IntroductionWindow introduction;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        introduction = new IntroductionWindow(pluginInterface, textures);
        windows.AddWindow(introduction);

        commandManager.AddHandler("/mom", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the MOM introduction and full-plugin installation instructions.",
        });
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenIntroduction;
        pluginInterface.UiBuilder.OpenConfigUi += OpenIntroduction;
    }

    private void OnCommand(string command, string arguments) => OpenIntroduction();

    private void OpenIntroduction() => introduction.IsOpen = true;

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenIntroduction;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenIntroduction;
        commandManager.RemoveHandler("/mom");
        windows.RemoveAllWindows();
    }
}
