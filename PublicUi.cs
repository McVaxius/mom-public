using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace mom.PublicShell;

internal sealed class PublicUi : IDisposable
{
    internal static readonly (string Code, string Name)[] Languages = [("en", "English"), ("de", "Deutsch"), ("fr", "Français"),
        ("es", "Español"), ("it", "Italiano"), ("ru", "Русский"), ("ja", "日本語"), ("ko", "한국어"), ("zh-Hans", "简体中文"),
        ("vi", "Tiếng Việt"), ("pt-BR", "Português (Brasil)"), ("id", "Bahasa Indonesia"), ("pl", "Polski"), ("tr", "Türkçe")];
    private readonly PublicPreferences preferences;
    private readonly ManagedUiFonts fonts;
    private readonly Dictionary<string, ResourceSet> sets = [];
    private ResourceSet current = null!;
    private MaterialTheme? theme;
    private uint accent;
    private uint draftAccent = uint.MaxValue;
    private Vector3 accentDraft;
    private bool frameCompact;
    private bool loggedFontIssue;
    internal PublicUi(IDalamudPluginInterface pi)
    {
        preferences = new(pi); fonts = new(pi.UiBuilder, "MOM public interface");
        foreach (var language in Languages)
        {
            var stream = typeof(PublicUi).Assembly.GetManifestResourceStream("mom.PublicShell.Strings." + language.Code + ".resources")
                ?? throw new MissingManifestResourceException(language.Code);
            sets.Add(language.Code, new ResourceSet(stream));
        }
        var keys = sets["en"].Cast<DictionaryEntry>().Select(e => (string)e.Key).ToArray();
        foreach (var (code, set) in sets)
            if (set.Cast<DictionaryEntry>().Count() != keys.Length || keys.Any(k => string.IsNullOrWhiteSpace(set.GetString(k))))
                throw new MissingManifestResourceException("Incomplete MOM public translations: " + code);
    }
    internal string T(string key) => current.GetString(key) ?? throw new MissingManifestResourceException(key);
    internal IDisposable Font(UiFontRole role) => fonts.Push(role);
    internal bool Compact => frameCompact;
    internal void Draw(Action draw, Action<Exception> report)
    {
        preferences.Reload();
        var language = sets.ContainsKey(preferences.Language) ? preferences.Language : "en";
        current = sets[language];
        fonts.Prepare(language, current.Cast<DictionaryEntry>().Select(e => (string)e.Value!).Concat(Languages.Select(l => l.Name)));
        UiStyle.Compact = frameCompact = preferences.Compact;
        var selected = preferences.Accent & 0xFFFFFF;
        if (theme == null || accent != selected) { accent = selected; theme = UiStyle.Theme(selected, true); }
        theme.Density = frameCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
        using var colors = MaterialTheme.Push(theme, ImGui.GetIO().FontGlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        if (!fonts.Ready())
        {
            if (!loggedFontIssue && fonts.Error is { } error) { report(error); loggedFontIssue = true; }
            ManagedUiFonts.DrawStatus(T(fonts.Error == null
                ? "Preparing MOM interface fonts..." : "MOM interface fonts are unavailable. See the Dalamud log.")); return;
        }
        using var geometry = UiStyle.Geometry(ImGui.GetIO().FontGlobalScale, true);
        using var body = fonts.Push(UiFontRole.Body);
        draw();
    }
    internal void Appearance()
    {
        if (draftAccent != preferences.Accent)
        {
            draftAccent = preferences.Accent;
            var rgb = UiStyle.Rgb(draftAccent);
            accentDraft = new(rgb.X, rgb.Y, rgb.Z);
        }
        var language = sets.ContainsKey(preferences.Language) ? preferences.Language : "en";
        using var action = Font(ImGui.GetWindowSize().X < 620 * MaterialTheme.Metrics.Scale ? UiFontRole.Caption : UiFontRole.Action);
        using var controls = MaterialControls.Push();
        var labels = new MaterialAppearanceLabels(T("Color"), T("Language"), T("Teal"), T("Blue"), T("Pink"), T("Custom RGB"));
        var accentChanged = MaterialAppearanceSelector.DrawAccent("mom-public-appearance", ref accentDraft, labels, frameCompact ? 28 : 40);
        var selectedName = Languages.Single(l => l.Code == language).Name;
        var minimum = MathF.Ceiling(ImGui.CalcTextSize(selectedName).X + MaterialControls.Metrics.Height
            + 3 * MaterialControls.Metrics.Gap + Math.Min(MaterialControls.Metrics.IconSize, MaterialControls.Metrics.Height));
        SameLineIfFits(minimum);
        var languageChanged = MaterialAppearanceSelector.DrawLanguage("mom-public-appearance", ref language,
            new(Languages.Select(l => new MaterialOption<string>(l.Code, l.Code, l.Name)).ToArray()), frameCompact ? 130 : 220);
        if (accentChanged) preferences.Accent = UiStyle.Pack(accentDraft);
        if (languageChanged) preferences.Language = language;
        SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("C").X);
        var compact = preferences.Compact;
        if (ImGui.Checkbox("C##mom-public-compact", ref compact)) { preferences.Compact = compact; preferences.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Compact mode"));
        if (accentChanged || languageChanged) preferences.Save();
    }
    private static void SameLineIfFits(float width)
    {
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X;
        if (right - ImGui.GetItemRectMax().X >= width + ImGui.GetStyle().ItemSpacing.X) ImGui.SameLine();
    }
    internal void Heading(string key)
    {
        using var heading = Font(UiFontRole.Heading);
        using var scale = new UiStyle.TextScale(25f * 11 / (13 * 18));
        Paragraph(key);
    }
    internal void Paragraph(string key) => Text(T(key));
    internal void Text(string text)
    {
        var window = ImGuiP.GetCurrentWindow();
        ImGui.PushTextWrapPos(window.Size.X - window.WindowPadding.X - window.ScrollbarSizes.X + window.Scroll.X);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
    public void Dispose() { fonts.Dispose(); foreach (var set in sets.Values) set.Dispose(); }
}
