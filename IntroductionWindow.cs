using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace mom.PublicShell;

internal sealed class IntroductionWindow : Window
{
    private const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    private const string SupportUrl = "https://ko-fi.com/mcvaxius";
    private static readonly Vector4 Accent = new(0.65f, 0.57f, 1f, 1f);
    private readonly ISharedImmediateTexture icon;
    private readonly ModuleLoader loader;
    private readonly Action refresh;

    public IntroductionWindow(IDalamudPluginInterface pi, ITextureProvider textures, ModuleLoader loader, Action refresh)
        : base($"MOM v{BuildInfo.Version}##Information")
    {
        this.loader = loader;
        this.refresh = refresh;
        Size = new Vector2(600, 530);
        SizeCondition = ImGuiCond.Appearing;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 380), MaximumSize = new Vector2(float.MaxValue) };
        icon = textures.GetFromFile(Path.Combine(pi.AssemblyLocation.DirectoryName!, "icon.png"));
    }

    public override void Draw()
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6 * scale);
        try
        {
            if (icon.TryGetWrap(out var texture, out _)) { ImGui.Image(texture.Handle, new Vector2(54) * scale); ImGui.SameLine(); }
            ImGui.BeginGroup();
            ImGui.TextColored(Accent, "M O M");
            ImGui.TextUnformatted("By DhogGPT");
            ImGui.EndGroup();
            ImGui.Separator();
            Card("About", "Welcome to MOM", "The public plugin is free. Additional functionality is distributed through privately granted access. Contact the community for availability and support.");
            ImGui.TextColored(Accent, "CONNECT WITH THE COMMUNITY");
            if (ImGui.Button("Join Discord", new Vector2(0, 32 * scale))) Util.OpenLink(DiscordUrl);
            ImGui.SameLine();
            if (ImGui.Button("Support on Ko-fi", new Vector2(0, 32 * scale))) Util.OpenLink(SupportUrl);
            Card("Community", "Help and discussion", "Visit The Dumpster Fire channel on Discord. Supporting the project does not automatically grant access.");
            Card("Access", "Already have an access download?", "Keep this public plugin installed and enabled. In APM, include mom in the plugin list, enable Advanced options, trust the publisher, and Ctrl+click the second refresh button with the direct mom access ZIP link on your clipboard. APM installs the access update and reloads MOM.");
            if (loader.Failed)
            {
                ImGui.TextColored(new Vector4(1f, .73f, .4f, 1f), "Access needs attention");
                ImGui.TextWrapped("Access could not be activated. Check the Dalamud log or contact the publisher for a compatible update.");
            }
            if (ImGui.Button("Refresh access", new Vector2(0, 28 * scale))) refresh();
        }
        finally { ImGui.PopStyleVar(3); }
    }

    private static void Card(string id, string title, string description)
    {
        var padding = ImGui.GetStyle().WindowPadding;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - padding.X * 2);
        var height = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.CalcTextSize(description, false, width).Y + padding.Y * 2;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.12f, .12f, .17f, .8f));
        try
        {
            var visible = ImGui.BeginChild(id, new Vector2(0, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            try { if (visible) { ImGui.TextColored(Accent, title); ImGui.TextWrapped(description); } }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.PopStyleColor(); }
    }
}
