using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace mom.PublicShell;

internal sealed class IntroductionWindow : Window
{
    private const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    private const string WebsiteUrl = "https://aethertek.io/";
    private const string SupportUrl = "https://ko-fi.com/mcvaxius";
    private static readonly Vector4 HeadingColor = new(0.74f, 0.58f, 0.96f, 1f);
    private static readonly string Version = typeof(Plugin).Assembly.GetName().Version!.ToString(4);
    private readonly ISharedImmediateTexture icon;

    public IntroductionWindow(IDalamudPluginInterface pluginInterface, ITextureProvider textures)
        : base($"MOM {Version}###mom.PublicShell.Introduction")
    {
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        icon = textures.GetFromFile(Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, "icon.png"));
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (icon.TryGetWrap(out var texture, out _))
        {
            ImGui.Image(texture.Handle, new Vector2(64) * scale);
            ImGui.SameLine();
        }
        ImGui.BeginGroup();
        ImGui.TextColored(HeadingColor, "MOM");
        ImGui.TextUnformatted($"Public introduction - {Version}");
        if (ImGui.SmallButton("Support on Ko-fi"))
            Util.OpenLink(SupportUrl);
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.09f, 0.13f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6) * scale);

        DrawPanel("Welcome to MOM",
            "This is the public introduction to MOM. It explains how to get the full plugin and install it with Aethertek Plugin Manager (APM).");
        ImGui.Spacing();
        DrawPanel("Get the full plugin",
            "Full-plugin access is arranged through Discord. Visit The Dumpster Fire channel for help and discussion.",
            showLinks: true);
        ImGui.Spacing();
        DrawPanel("Install the full plugin with APM",
            "1. Copy the full-plugin ZIP URL from Discord (mom.zip or mom-v<version>.zip).\n" +
            "2. Open /apm and check I trust the publisher.\n" +
            "3. Select mom in APM's development-plugin list.\n" +
            "4. Choose Update selected plugin from clipboard.\n\n" +
            "Keep APM enabled until the update finishes. The full plugin replaces this introduction and opens with /mom.");

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor();
    }
    private static void DrawPanel(string title, string text, bool showLinks = false)
    {
        var style = ImGui.GetStyle();
        var textWidth = ImGui.GetContentRegionAvail().X - style.WindowPadding.X * 2f;
        var buttonHeight = 32f * ImGuiHelpers.GlobalScale;
        var height = style.WindowPadding.Y * 2f + ImGui.GetTextLineHeight() +
                     style.ItemSpacing.Y + ImGui.CalcTextSize(text, false, textWidth).Y;
        if (showLinks)
            height += style.ItemSpacing.Y + buttonHeight;

        if (ImGui.BeginChild(title, new Vector2(0, height), false,
                ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(HeadingColor, title);
            ImGui.TextWrapped(text);
            if (showLinks)
            {
                var buttonSize = new Vector2((ImGui.GetContentRegionAvail().X - style.ItemSpacing.X) / 2f, buttonHeight);
                if (ImGui.Button("Join Discord", buttonSize))
                    Util.OpenLink(DiscordUrl);
                ImGui.SameLine();
                if (ImGui.Button("Visit Website", buttonSize))
                    Util.OpenLink(WebsiteUrl);
            }
        }
        ImGui.EndChild();
    }

}
