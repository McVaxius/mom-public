using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace mom.PublicShell;

internal sealed class PublicUi : IDisposable
{
    internal static readonly (string Code, string Name)[] Languages = [("en", "English"), ("de", "Deutsch"), ("fr", "Français"),
        ("es", "Español"), ("it", "Italiano"), ("ru", "Русский"), ("ja", "日本語"), ("ko", "한국어"), ("zh-Hans", "简体中文"),
        ("vi", "Tiếng Việt"), ("pt-BR", "Português (Brasil)"), ("id", "Bahasa Indonesia"), ("pl", "Polski"), ("tr", "Türkçe"), ("hi", "हिन्दी")];
    private readonly PublicPreferences preferences;
    private readonly MaterialWindowOpacity fontStatusOpacity = new();
    private readonly ManagedUiFonts fonts;
    private readonly MaterialTextHost shapedText;
    private readonly Dictionary<string, ResourceSet> sets = [];
    private ResourceSet current = null!;
    private MaterialTheme? theme;
    private uint accent;
    private uint draftAccent = uint.MaxValue;
    private Vector3 accentDraft;
    private bool frameCompact;
    private bool loggedFontIssue;
    private bool appearanceRequested;
    internal PublicUi(IDalamudPluginInterface pi, ITextureProvider textures)
    {
        preferences = new(pi); shapedText = new(textures); fonts = new(pi.UiBuilder, "MOM public interface", shapedText.Renderer, publicHost: true);
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
    internal IDisposable WindowBody(bool narrow)
        => fonts.PushWindowBody(!narrow, (frameCompact ? narrow ? 14 : 14.4f : narrow ? 15 : 24) / 11);
    internal bool Compact => frameCompact;
    internal void RequestAppearance() => appearanceRequested = true;
    internal void Draw(Action draw, Action<Exception> report)
    {
        using var shaping = shapedText.Push();
        preferences.Reload();
        var language = sets.ContainsKey(preferences.Language) ? preferences.Language : "en";
        current = sets[language];
        UiStyle.Compact = frameCompact = preferences.Compact;
        fonts.PrepareForDensity(language, current.Cast<DictionaryEntry>().Select(e => (string)e.Value!)
            .Concat(Languages.Where(l => l.Code != "hi").Select(l => l.Name)), frameCompact);
        var selected = preferences.Accent & 0xFFFFFF;
        if (theme == null || accent != selected) { accent = selected; theme = UiStyle.Theme(selected, true); }
        theme.Density = frameCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
        using var colors = MaterialTheme.Push(theme, ImGui.GetIO().FontGlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        if (!fonts.Ready())
        {
            if (!loggedFontIssue && fonts.Error is { } error) { report(error); loggedFontIssue = true; }
            var hindiFailed = language == "hi" && fonts.Error is not null;
            ManagedUiFonts.DrawStatusWithRecovery(language == "hi"
                ? hindiFailed ? "Hindi is unavailable. Use English to recover; your saved language is unchanged." : "Preparing MOM interface fonts..."
                : T(fonts.Error == null ? "Preparing MOM interface fonts..." : "MOM interface fonts are unavailable. See the Dalamud log."),
                hindiFailed ? () => { preferences.Language = "en"; preferences.Save(); } : null);
            ApplyWindowOpacity(fontStatusOpacity, "MOM##FontStatus"); return;
        }
        using var geometry = UiStyle.Geometry(ImGui.GetIO().FontGlobalScale, true);
        using var body = fonts.Push(UiFontRole.Body);
        draw();
    }
    internal void ApplyWindowOpacity(MaterialWindowOpacity opacity, string windowName)
        => opacity.Apply(windowName, preferences.UiWindowOpacityPercent / 100f, preferences.UiTransparencyEnabled,
            preferences.UiAutoFade, preferences.UiFadedOpacityPercent / 100f, preferences.UiUnfocusedDelaySeconds);

    internal void Appearance()
    {
        if (draftAccent != preferences.Accent)
        {
            draftAccent = preferences.Accent;
            var rgb = UiStyle.Rgb(draftAccent);
            accentDraft = new(rgb.X, rgb.Y, rgb.Z);
        }
        var root = ImGui.GetID("");
        var language = sets.ContainsKey(preferences.Language) ? preferences.Language : "en";
        using var action = Font(ImGui.GetWindowSize().X < 620 * MaterialTheme.Metrics.Scale ? UiFontRole.Caption : UiFontRole.Action);
        using var controls = MaterialControls.Push();
        var options = new MaterialOptions<string>(Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
            l.Code == "hi" && !fonts.HindiAvailable ? "Hindi (unavailable)" : l.Name, l.Code == "hi" && !fonts.HindiAvailable)).ToArray());
        if (preferences.UiLanguageVisibleOnMainWindow)
        {
            var languageChanged = MaterialAppearanceSelector.DrawLanguage("mom-public-appearance", ref language, options, frameCompact ? 130 : 220);
            if (languageChanged) { preferences.Language = language; preferences.Save(); }
        }
        if (preferences.UiCompactVisibleOnMainWindow)
        {
            SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure("C").X);
            var compact = preferences.Compact;
            if (UiStyle.NativeCheckbox("C##mom-public-compact", ref compact)) { preferences.Compact = compact; preferences.Save(); }
            if (ImGui.IsItemHovered()) MaterialText.SetTooltip(T("Compact mode"));
        }
        if (preferences.UiTransparencyVisibleOnMainWindow)
        {
            SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure(T("Transparency")).X);
            var enabled = preferences.UiTransparencyEnabled;
            if (UiStyle.NativeCheckbox(T("Transparency") + "###window-transparency-main", ref enabled))
            { preferences.UiTransparencyEnabled = enabled; preferences.Save(); }
        }
        SameLineIfFits(MaterialControls.Metrics.Height);
        if (MaterialButton.IconButton("window-settings", MaterialIcon.Settings))
            ImGui.OpenPopup("mom-public-window-appearance");
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(T("Window appearance"));
        if (appearanceRequested)
        {
            appearanceRequested = false;
            ImGui.OpenPopup("mom-public-window-appearance");
        }
        ImGui.SetNextWindowSize(new Vector2(0, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("mom-public-window-appearance")) return;
        MaterialText.Text(T("Window appearance")); ImGui.Separator();
        // Keep the moved colour action on its original root; new settings controls own popup IDs.
        ImGuiP.PushOverrideID(root);
        var accentChanged = MaterialAppearanceSelector.DrawAccent("mom-public-appearance", ref accentDraft,
            new(T("Color"), T("Language"), T("Teal"), T("Blue"), T("Pink"), T("Custom RGB")), frameCompact ? 28 : 40);
        ImGui.PopID();
        if (accentChanged) { preferences.Accent = UiStyle.Pack(accentDraft); preferences.Save(); }
        var settingsCompact = preferences.Compact;
        if (UiStyle.NativeCheckbox(T("Compact mode") + "###window-compact-settings", ref settingsCompact))
        { preferences.Compact = settingsCompact; preferences.Save(); }
        if (MaterialAppearanceSelector.DrawLanguage("mom-public-settings", ref language, options, 180))
        { preferences.Language = language; preferences.Save(); }
        DrawWindowSettings();
        ImGui.EndPopup();
    }

    private void DrawWindowSettings()
    {
        var changed = false;
        var compactVisible = preferences.UiCompactVisibleOnMainWindow;
        if (UiStyle.NativeCheckbox(T("Compact visible on main window") + "###window-compact-visible", ref compactVisible))
        { preferences.UiCompactVisibleOnMainWindow = compactVisible; changed = true; }
        var transparencyVisible = preferences.UiTransparencyVisibleOnMainWindow;
        if (UiStyle.NativeCheckbox(T("Transparency visible on main window") + "###window-transparency-visible", ref transparencyVisible))
        { preferences.UiTransparencyVisibleOnMainWindow = transparencyVisible; changed = true; }
        var languageVisible = preferences.UiLanguageVisibleOnMainWindow;
        if (UiStyle.NativeCheckbox(T("Language visible on main window") + "###window-language-visible", ref languageVisible))
        { preferences.UiLanguageVisibleOnMainWindow = languageVisible; changed = true; }
        var enabled = preferences.UiTransparencyEnabled;
        if (UiStyle.NativeCheckbox(T("Transparency") + "###window-transparency", ref enabled))
        { preferences.UiTransparencyEnabled = enabled; changed = true; }
        ImGui.BeginDisabled(!preferences.UiTransparencyEnabled);
        ImGui.SetNextItemWidth(96 * MaterialTheme.Metrics.Scale);
        var normal = preferences.UiWindowOpacityPercent;
        if (UiStyle.NativeInputInt(T("Opacity (%)") + "###window-opacity", ref normal))
        { preferences.UiWindowOpacityPercent = normal; changed = true; }
        var autoFade = preferences.UiAutoFade;
        if (UiStyle.NativeCheckbox(T("Auto-fade when unfocused") + "###window-auto-fade", ref autoFade))
        { preferences.UiAutoFade = autoFade; changed = true; }
        ImGui.BeginDisabled(!preferences.UiAutoFade);
        ImGui.SetNextItemWidth(96 * MaterialTheme.Metrics.Scale);
        var faded = preferences.UiFadedOpacityPercent;
        if (UiStyle.NativeInputInt(T("Unfocused opacity (%)") + "###window-faded-opacity", ref faded))
        { preferences.UiFadedOpacityPercent = faded; changed = true; }
        ImGui.SetNextItemWidth(96 * MaterialTheme.Metrics.Scale);
        var delay = preferences.UiUnfocusedDelaySeconds;
        if (UiStyle.NativeInputInt(T("Unfocused delay (seconds)") + "###window-unfocused-delay", ref delay))
        { preferences.UiUnfocusedDelaySeconds = delay; changed = true; }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (changed) preferences.Save();
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
        MaterialText.Text(text);
        ImGui.PopTextWrapPos();
    }
    public void Dispose() { fonts.Dispose(); shapedText.Dispose(); foreach (var set in sets.Values) set.Dispose(); }
}
