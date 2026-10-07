using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

#if MOM_PRIVATE_UI
namespace mom.PrivateUi;
#else
namespace mom.PublicShell;
#endif

internal enum UiFontRole { Body, BodyStrong, Title, Caption, Small, Heading, Action }

internal static class UiStyle
{
    internal static readonly float[] FontSizes = [11, 12, 24, 10, 9, 13, 11];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "segoeui.ttf", "segoeui.ttf", "seguisb.ttf", "seguisb.ttf"];
    internal readonly ref struct TextScale
    {
        private readonly float previous;
        internal TextScale(float multiplier)
        {
            previous = ImGuiP.GetCurrentWindow().FontWindowScale;
            ImGui.SetWindowFontScale(previous * multiplier);
        }
        public void Dispose() => ImGui.SetWindowFontScale(previous);
    }
    internal static bool Compact { get; set; }
    internal static float PrivateTextScale => Compact ? 1.25f : 15f / 11;
    internal static float Padding => Compact ? 10 : 14;
    internal static float Gap => Compact ? 10 : 14;
    internal static Vector4 Ready => Rgb(0x24E6CB);
    internal static Vector4 Warning => Rgb(0xFFBF68);
    internal static Vector4 Error => Rgb(0xFF586B);
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    internal static uint Pack(Vector3 rgb) => ((uint)Math.Clamp((int)MathF.Round(rgb.X * 255), 0, 255) << 16)
        | ((uint)Math.Clamp((int)MathF.Round(rgb.Y * 255), 0, 255) << 8) | (uint)Math.Clamp((int)MathF.Round(rgb.Z * 255), 0, 255);
    internal static MaterialTheme Theme(uint accent, bool publicHost)
    {
        var referenceRgb = publicHost ? 0xA475FFu : 0x00DFE6u;
        accent &= 0xFFFFFF;
        var reference = Rgb(referenceRgb);
        var original = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(reference.X, reference.Y, reference.Z)));
        var selected = Rgb(accent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == referenceRgb) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, seed.Y < .001f ? 0 : lch.Y * seed.Y / original.Y,
                lch.Z + (seed.Y < .001f ? 0 : seed.Z - original.Z)), 1);
        }
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var background = Relative(publicHost ? 0x161B24u : 0x0A1923u);
        var foreground = Relative(0xF2F4FB);
        var primary = Relative(referenceRgb);
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground,
            Surface = Relative(publicHost ? 0x191F29u : 0x0C1B24u), OnSurface = foreground,
            SurfaceContainerLowest = Relative(publicHost ? 0x151A23u : 0x0C1D26u),
            SurfaceContainerLow = Relative(publicHost ? 0x191F29u : 0x0C1B24u),
            SurfaceContainer = Relative(publicHost ? 0x202735u : 0x152A37u),
            SurfaceContainerHigh = Relative(publicHost ? 0x242C3Au : 0x1D3443u),
            SurfaceContainerHighest = Relative(publicHost ? 0x2C3544u : 0x243F4Eu),
            SurfaceVariant = Relative(publicHost ? 0x354052u : 0x2C4D5Cu),
            OnSurfaceVariant = Relative(publicHost ? 0xBDC8DDu : 0xC6D9E5u),
            Outline = Relative(publicHost ? 0x465269u : 0x496677u),
            OutlineVariant = Relative(publicHost ? 0x323E50u : 0x294854u),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(publicHost ? 0x5040D5u : 0x005D68u), OnPrimaryContainer = foreground,
            Secondary = Relative(publicHost ? 0xB9A8ECu : 0xA5DCE0u), OnSecondary = background,
            SecondaryContainer = Relative(publicHost ? 0x242C3Au : 0x1D3443u), OnSecondaryContainer = foreground,
            Tertiary = Relative(publicHost ? 0xCDBCEEu : 0xB4D7E7u), OnTertiary = background,
            TertiaryContainer = Relative(publicHost ? 0x2C3544u : 0x243F4Eu), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(publicHost ? 0x6542A9u : 0x167E87u),
        };
        return new(colors) { SurfaceOpacity = 1 };
    }
    internal static MaterialStyleScope Geometry(float scale, bool publicHost = false)
    {
        var scope = new MaterialStyleScope();
        scope.Style(ImGuiStyleVar.WindowPadding, new Vector2(publicHost ? Compact ? 14 : 28 : Padding) * scale);
        scope.Style(ImGuiStyleVar.FramePadding, new Vector2(Compact ? 6 : publicHost ? 12 : 8, Compact ? 3 : publicHost ? 8 : 5) * scale);
        scope.Style(ImGuiStyleVar.ItemSpacing, new Vector2(publicHost ? Compact ? 10 : 20 : Compact ? 8 : 12, publicHost ? Compact ? 7 : 14 : Compact ? 5 : 7) * scale);
        scope.Style(ImGuiStyleVar.CellPadding, new Vector2(Compact ? 6 : 10, Compact ? 4 : 6) * scale);
        scope.Style(ImGuiStyleVar.FrameRounding, 4 * scale);
        scope.Style(ImGuiStyleVar.ChildRounding, 4 * scale);
        scope.Style(ImGuiStyleVar.FrameBorderSize, scale);
        return scope;
    }
    internal static void Panel(string id, uint root, Vector2 size, Action draw, bool raised = false, float? padding = null, float radius = 4)
    {
        var callerFontSize = ImGui.GetFontSize();
        var min = ImGui.GetCursorScreenPos();
        var extent = new Vector2(size.X <= 0 ? ImGui.GetContentRegionAvail().X : size.X, size.Y);
        var colors = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(min, min + extent, raised ? colors.SurfaceContainerHigh : colors.Surface, colors.Background, radius * MaterialTheme.Metrics.Scale);
        ImGui.GetWindowDrawList().AddRect(min, min + extent, MaterialCanvas.Color(colors.OutlineVariant), radius * MaterialTheme.Metrics.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2((padding ?? Padding) * MaterialTheme.Metrics.Scale));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        try
        {
            if (ImGui.BeginChild(id, size, false, ImGuiWindowFlags.AlwaysUseWindowPadding|ImGuiWindowFlags.HorizontalScrollbar))
            {
                using var textScale = new TextScale(callerFontSize / ImGui.GetFontSize());
                ImGuiP.PushOverrideID(root);
                try { draw(); } finally { ImGui.PopID(); }
            }
            ImGui.EndChild();
        }
        finally { ImGui.PopStyleColor(); ImGui.PopStyleVar(); }
    }
    internal static bool NativeButton(string native, string visible, Vector2 size, bool primary = false, MaterialIcon icon = MaterialIcon.None)
    {
        var toolbar = size.Y <= 0 && MaterialControls.Context != MaterialControlContext.Dense && ImGui.GetStyle().FramePadding.Y > 0;
        using var height = toolbar ? MaterialText.PushLineHeight(visible) : default;
        using var controls = toolbar ? MaterialControls.Push(MaterialControlContext.Toolbar) : default;
        var colors = MaterialTheme.Current.Colors;
        if (primary)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, colors.PrimaryContainer);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(colors.PrimaryContainer, colors.Primary, .25f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(colors.PrimaryContainer, colors.Primary, .4f));
        }
        // Keep the raw label's native ID, including buttons predating keyed localization.
        var iconWidth = icon == MaterialIcon.None ? 0 : ImGui.GetFontSize() * 1.7f;
        var iconSize = icon == MaterialIcon.None ? 0 : ImGui.GetFontSize() * 1.3f;
        var textSize = MaterialText.Measure(visible);
        size.X=MaterialLayout.FitNextItemWidth(size.X,textSize.X+ImGui.GetStyle().FramePadding.X*2+iconWidth);
        if (toolbar)
            size.Y = MaterialControlMetrics.Measure(MaterialTheme.Metrics, Math.Max(ImGui.GetTextLineHeight(), Math.Max(textSize.Y, iconSize)), MaterialControlContext.Toolbar).Height;
        else if (MaterialText.RequiresShaping(visible))
            size.Y = Math.Max(size.Y, textSize.Y + 2 * ImGui.GetStyle().FramePadding.Y);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var pressed = ImGui.Button(native, size);
        ImGui.PopStyleColor();
        if (primary) ImGui.PopStyleColor(3);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var position = min + new Vector2((max.X - min.X - textSize.X - iconWidth) * .5f, (max.Y - min.Y - textSize.Y) * .5f);
        var ink = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        if (ImGui.GetStyle().Alpha < 1) ink.W *= ImGui.GetStyle().Alpha;
        if (icon != MaterialIcon.None) MaterialIcons.Draw(icon, toolbar ? new Vector2(position.X, min.Y + (max.Y - min.Y - iconSize) * .5f) : position, iconSize, ink);
        MaterialText.AddText(ImGui.GetWindowDrawList(), ImGui.GetFont(), ImGui.GetFontSize(), position + new Vector2(iconWidth, 0), ImGui.ColorConvertFloat4ToU32(ink), visible);
        return pressed;
    }

    internal static bool NativeCheckbox(string native, ref bool value)
    {
        var visible = native.Split("##", 2)[0];
        if (!MaterialText.RequiresShaping(visible)) return ImGui.Checkbox(native, ref value);
        using var height = MaterialText.PushLineHeight(visible);
        var text = MaterialText.Measure(visible);
        var suffix = native.IndexOf("###", StringComparison.Ordinal);
        // Include the raster's final antialias pixel in the native hit area; ### retains its ID.
        var reservedWidth = MathF.Ceiling(text.X) + 1;
        var spaces = new string(' ', (int)MathF.Ceiling(reservedWidth / Math.Max(1, ImGui.CalcTextSize(" ").X)));
        while (ImGui.CalcTextSize(spaces).X < reservedWidth) spaces += " ";
        var proxy = suffix < 0 ? native : spaces + native[suffix..];
        var ink = ImGui.GetColorU32(ImGuiCol.Text);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        bool changed;
        try { changed = ImGui.Checkbox(proxy, ref value); }
        finally { ImGui.PopStyleColor(); }
        if (ImGui.IsItemVisible())
        {
            var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
            var draw = ImGui.GetWindowDrawList();
            draw.PushClipRect(min, max, true);
            try { MaterialText.AddText(draw, min + new Vector2(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X, (max.Y - min.Y - text.Y) * .5f), ink, visible); }
            finally { draw.PopClipRect(); }
        }
        return changed;
    }

    internal static bool NativeInputInt(string native, ref int value)
    {
        var visible = native.Split("##", 2)[0];
        var suffix = native.IndexOf("###", StringComparison.Ordinal);
        if (!MaterialText.RequiresShaping(visible) || suffix < 0) return ImGui.InputInt(native, ref value);
        var width = ImGui.CalcItemWidth();
        MaterialText.Text(visible);
        ImGui.SetNextItemWidth(width);
        return ImGui.InputInt(native[suffix..], ref value);
    }

    internal static bool NativeCombo(string native, ref int selected, string[] options, int count)
    {
        if (!options.Take(count).Any(MaterialText.RequiresShaping)) return ImGui.Combo(native, ref selected, options, count);
        var changed = false;
        var id = ImGui.GetID(native);
        if (MaterialText.BeginCombo(native, selected >= 0 && selected < count ? options[selected] : ""))
        {
            try
            {
                for (var index = 0; index < count; index++)
                {
                    ImGui.PushID(index);
                    try
                    {
                        var active = index == selected;
                        if (MaterialText.Selectable(options[index], active)) { selected = index; changed = true; }
                        if (active) ImGui.SetItemDefaultFocus();
                    }
                    finally { ImGui.PopID(); }
                }
            }
            finally { ImGui.EndCombo(); }
        }
        if (changed) ImGuiP.MarkItemEdited(id);
        return changed;
    }
}
