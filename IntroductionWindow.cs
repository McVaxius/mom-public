using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace mom.PublicShell;

internal sealed class IntroductionWindow : Window
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private readonly MaterialWindowOpacity windowOpacity = new();
    private const string DiscordUrl = "https://discord.gg/VsXqydsvpu";
    private const string SupportUrl = "https://ko-fi.com/mcvaxius";
    private readonly ISharedImmediateTexture icon;
    private readonly ModuleLoader loader;
    private readonly Action refresh;
    private readonly PublicUi ui;
    public IntroductionWindow(IDalamudPluginInterface pi, ITextureProvider textures, ModuleLoader loader, Action refresh, PublicUi ui)
        : base($"MOM v{BuildInfo.Version}##Information")
    {
        this.loader = loader; this.refresh = refresh; this.ui = ui;
        Size = new Vector2(1472, 932);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 380), MaximumSize = new Vector2(float.MaxValue) };
        icon = textures.GetFromFile(Path.Combine(pi.AssemblyLocation.DirectoryName!, "icon.png"));
    }
    public override void PreDraw() => motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    public override void PostDraw()
    {
        motion.Restore(this);
        ui.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        motion.DrawChrome();
        var narrow = ImGui.GetWindowSize().X < 620 * ImGui.GetIO().FontGlobalScale;
        using var scale = new UiStyle.TextScale((ui.Compact ? narrow ? 14 : 14.4f : narrow ? 15 : 24) / 11);
        DrawContent();
    }
    private void DrawContent()
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        var root = ImGui.GetID("");
        ImGui.BeginGroup();
        if (icon.TryGetWrap(out var texture, out _)) { ImGui.Image(texture.Handle, new Vector2(ui.Compact ? 32 : 44) * scale); ImGui.SameLine(); }
        using (ui.Font(UiFontRole.Title))
        {
            using var titleScale = new UiStyle.TextScale(42f * 11 / (24 * 18));
            ImGui.TextColored(MaterialTheme.Current.Colors.Primary, "M");
            ImGui.SameLine(0, ImGui.CalcTextSize(" ").X + (ui.Compact ? 6 : 10) * scale);
            ImGui.TextColored(MaterialTheme.Current.Colors.Primary, "O");
            ImGui.SameLine(0, ImGui.CalcTextSize(" ").X + (ui.Compact ? 6 : 10) * scale);
            ImGui.TextColored(MaterialTheme.Current.Colors.Primary, "M");
        }
        ImGui.EndGroup();
        if (ImGui.GetContentRegionAvail().X > 550 * scale) ImGui.SameLine();
        ImGui.BeginGroup();
        using (ui.Font(UiFontRole.Caption))
        {
            ImGui.TextDisabled("v" + BuildInfo.Version);
            ImGui.TextDisabled(ui.T("By DhogGPT"));
        }
        ImGui.EndGroup();
        if (ImGui.GetContentRegionAvail().X > 480 * scale)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X - (ui.Compact ? 290 : 470) * scale));
        }
        ui.Appearance();
        ui.Paragraph("Public access host");
        ImGui.Separator();
        var columns = ImGui.GetContentRegionAvail().X >= (ui.Compact ? 780 : 1230) * scale ? 3 : 1;
        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * (columns - 1)) / columns;
        var height = MathF.Max((ui.Compact ? 390 : 646) * scale, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - 24 * scale);
        Card("About", root, width, height, MaterialIcon.Group, () =>
        {
            ui.Heading("Welcome to MOM");
            ui.Paragraph("Welcome");
            ImGui.Spacing(); ui.Paragraph("AccessBoundary");
            ImGui.Spacing(); ImGui.Separator();
            using (ui.Font(UiFontRole.Caption)) ui.Paragraph("CommunityCredit");
        });
        if (columns > 1) ImGui.SameLine();
        Card("Community", root, width, height, MaterialIcon.Chat, () =>
        {
            ui.Heading("Help and discussion");
            ui.Paragraph("CommunityHelp");
            ImGui.Spacing();
            using (ui.Font(ImGui.GetContentRegionAvail().X < 300 * scale ? UiFontRole.Caption : UiFontRole.Action))
            {
                if (CommunityButton("Join Discord", ui.T("Join Discord"), new(-1, (ui.Compact ? 48 : 76) * scale), discord: true)) Util.OpenLink(DiscordUrl);
                if (CommunityButton("Support on Ko-fi", ui.T("Support on Ko-fi"), new(-1, (ui.Compact ? 48 : 76) * scale), discord: false)) Util.OpenLink(SupportUrl);
            }
            ui.Paragraph("SupportBoundary");
        });
        if (columns > 1) ImGui.SameLine();
        Card("Access", root, width, height, MaterialIcon.Download, () =>
        {
            ui.Heading("Already have an access download?");
            ui.Paragraph("InstallAccess");
            ImGui.Spacing();
            using (ui.Font(ImGui.GetContentRegionAvail().X < 300 * scale ? UiFontRole.Caption : UiFontRole.Action))
                if (UiStyle.NativeButton("Refresh access", ui.T("Refresh access"), new(-1, (ui.Compact ? 48 : 76) * scale), true, MaterialIcon.Refresh)) refresh();
            ui.Paragraph("RefreshExplanation");
            if (loader.Failed)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiStyle.Warning);
                ui.Paragraph("Access needs attention");
                ImGui.PopStyleColor();
                ui.Paragraph("AccessFailure");
            }
        });
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, loader.Failed ? UiStyle.Warning : loader.Module != null ? UiStyle.Ready : MaterialTheme.Current.Colors.OnSurfaceVariant);
        ui.Text(ui.T("Access status") + "  ·  " + ui.T(loader.Failed ? "Access needs attention" : loader.Module != null ? "Active" : "No access module installed"));
        ImGui.PopStyleColor();
    }
    private static bool CommunityButton(string native, string visible, Vector2 size, bool discord)
    {
        var colors = MaterialTheme.Current.Colors;
        var fontSize = ImGui.GetFontSize();
        var logoSize = fontSize * 2;
        var logoWidth = fontSize * 2.4f;
        var textSize = ImGui.CalcTextSize(visible);
        size.X = MaterialLayout.FitNextItemWidth(size.X, textSize.X + ImGui.GetStyle().FramePadding.X * 2 + logoWidth);
        if (discord)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, colors.PrimaryContainer);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(colors.PrimaryContainer, colors.Primary, .25f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(colors.PrimaryContainer, colors.Primary, .4f));
        }
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        bool pressed;
        try { pressed = ImGui.Button(native, size); }
        finally
        {
            ImGui.PopStyleColor();
            if (discord) ImGui.PopStyleColor(3);
        }
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var centerY = (min.Y + max.Y) * .5f;
        var x = min.X + (max.X - min.X - textSize.X - logoWidth) * .5f;
        var alpha = ImGui.GetStyle().Alpha;
        var ink = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ink.W *= alpha;
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(min, max, true);
        try
        {
            var origin = new Vector2(x, centerY - logoSize * .5f);
            MaterialIcons.Draw(discord ? MaterialIcon.Discord : MaterialIcon.KoFi, drawList, origin, logoSize, Vector4.One, alpha);
            drawList.AddText(ImGui.GetFont(), fontSize, new(x + logoWidth, centerY - textSize.Y * .5f), ImGui.ColorConvertFloat4ToU32(ink), visible);
        }
        finally { drawList.PopClipRect(); }
        return pressed;
    }
    private void Card(string id, uint root, float width, float height, MaterialIcon symbol, Action draw)
    {
        UiStyle.Panel(id, root, new(width, height), () =>
        {
            var pos = ImGui.GetCursorScreenPos();
            MaterialIcons.Draw(symbol, pos, (ui.Compact ? 34 : 80) * ImGui.GetIO().FontGlobalScale, MaterialTheme.Current.Colors.Primary);
            ImGui.Dummy(new Vector2((ui.Compact ? 40 : 92) * ImGui.GetIO().FontGlobalScale));
            draw();
        }, padding: ui.Compact ? 14 : 32, radius: ui.Compact ? 4 : 8);
    }
}
