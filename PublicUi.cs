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
    private MaterialColorSettings colorDraft = new();
    private MaterialColorSettings? savedColors;
    private MaterialColorSettings? appliedColors;
    private bool frameCompact;
    private bool loggedFontIssue;
    private bool appearanceRequested;
    private MaterialUiSoundPlayer? uiSounds;
    private Dalamud.Interface.Windowing.TitleBarButton? soundButton;
    private bool soundEnabled;
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
        if (savedColors != preferences.Colors) { colorDraft = preferences.Colors; savedColors = preferences.Colors; }
        if (theme == null || accent != selected || appliedColors != colorDraft)
        { accent = selected; appliedColors = colorDraft; theme = UiStyle.Theme(selected, true, colorDraft); }
        theme.Density = frameCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
        theme.Feedback = PlaySound;
        var enabled = theme.IsLcars && !preferences.UiLcarsSoundsMuted;
        if (soundEnabled && !enabled) uiSounds?.Stop();
        soundEnabled = enabled;
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

    internal MaterialAppearanceTitleBar RegisterTitleBar(Dalamud.Interface.Windowing.Window owner)
        => MaterialAppearanceTitleBar.Register(owner,
            () => new(T("Language"), T("Compact mode"), T("Transparency"), T("Color")),
            DrawTitleLanguage, () => { preferences.Compact = !preferences.Compact; preferences.Save(); }, DrawWindowSettings, DrawTitleColour,
            () => new(preferences.UiLanguageVisibleOnMainWindow, preferences.UiCompactVisibleOnMainWindow, preferences.UiTransparencyVisibleOnMainWindow, true));

    private void DrawTitleLanguage()
    {
        var language = sets.ContainsKey(preferences.Language) ? preferences.Language : "en";
        using var action = Font(UiFontRole.Action);
        var options = new MaterialOptions<string>(Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
            l.Code == "hi" && !fonts.HindiAvailable ? "Hindi (unavailable)" : l.Name, l.Code == "hi" && !fonts.HindiAvailable)).ToArray());
        if (MaterialAppearanceSelector.DrawLanguage("mom-public-titlebar-language", ref language, options, 180))
        { preferences.Language = language; preferences.Save(); }
    }

    private void DrawTitleColour()
    {
        if (draftAccent != preferences.Accent)
        { draftAccent = preferences.Accent; var rgb = UiStyle.Rgb(draftAccent); accentDraft = new(rgb.X, rgb.Y, rgb.Z); }
        using var action = Font(UiFontRole.Action);
        DrawColourEditor("mom-public-titlebar-colour");
    }

    private void DrawColourEditor(string id)
    {
        if (!MaterialAppearanceSelector.DrawColors(id, ref colorDraft, ref accentDraft,
            new(T("Color"), T("Language"), T("Teal"), T("Blue"), T("Pink"), T("Custom RGB")),
            new(T("Auto"), T("Picker"), T("Default"), T("Background, controls and text follow your theme."),
                T("Theme colour"), T("Choose each colour independently."), T("Background"), T("Controls"),
                T("Text"), T("Aa  Preview text"), T("Control"),
                T("Switch to Picker to set each colour yourself."), T("Switch back to Auto to use the automatic theme."))
                { Lcars = T("LCARS"), LcarsHelp = T("LCARS panels, bars and controls") },
            frameCompact ? 298 : 316)) return;
        preferences.Colors = colorDraft;
        if (colorDraft.Mode != MaterialColorMode.Lcars) { uiSounds?.Stop(); soundEnabled = false; }
        preferences.Accent = MaterialColorSettings.ToRgb(accentDraft);
        preferences.Save();
    }

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
        DrawColourEditor("mom-public-appearance");
        ImGui.PopID();
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
        ImGui.BeginDisabled(preferences.Colors.Mode != MaterialColorMode.Lcars);
        try
        {
            var sounds = !preferences.UiLcarsSoundsMuted;
            if (UiStyle.NativeCheckbox(T("UI sounds") + "###mom-public-ui-sounds", ref sounds)) SetSoundsMuted(!sounds);
            ImGui.BeginDisabled(preferences.UiLcarsSoundsMuted);
            try
            {
                var current = MaterialTheme.Current;
                var feedback = current.Feedback;
                bool preview;
                try { current.Feedback = null; preview = UiStyle.NativeButton("mom-public-ui-sound-preview", T("Preview sound"), default); }
                finally { current.Feedback = feedback; }
                MaterialLcars.Feedback(preview, MaterialUiSoundCue.Preview);
            }
            finally { ImGui.EndDisabled(); }
            if (uiSounds?.Error is not null) Text(T("Audio output is unavailable."));
        }
        finally { ImGui.EndDisabled(); }
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
        if (MaterialTheme.Current.IsLcars) MaterialLcars.PanelHeading(T(key), 1);
        else Paragraph(key);
    }
    internal void Paragraph(string key) => Text(T(key));
    internal void Text(string text)
    {
        var window = ImGuiP.GetCurrentWindow();
        ImGui.PushTextWrapPos(window.Size.X - window.WindowPadding.X - window.ScrollbarSizes.X + window.Scroll.X);
        MaterialText.Text(text);
        ImGui.PopTextWrapPos();
    }
    private void PlaySound(MaterialUiSoundCue cue)
    {
        if (preferences.Colors.Mode == MaterialColorMode.Lcars && !preferences.UiLcarsSoundsMuted)
            (uiSounds ??= new()).TryPlay(cue);
    }
    private void SetSoundsMuted(bool muted)
    {
        preferences.UiLcarsSoundsMuted = muted;
        preferences.Save();
        if (muted) uiSounds?.Stop();
    }
    internal void PrepareTitleBar(Dalamud.Interface.Windowing.Window owner, MaterialAppearanceTitleBar actions)
    {
        var lcars = MaterialTheme.Current.IsLcars;
        actions.SetLcarsIcons(lcars);
        soundButton ??= new()
        {
            Priority = (int)Math.Max(int.MinValue + 1L, owner.TitleBarButtons.Select(button => (long)button.Priority).DefaultIfEmpty(0).Min() - 1),
            IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left && preferences.Colors.Mode == MaterialColorMode.Lcars) SetSoundsMuted(!preferences.UiLcarsSoundsMuted); },
            ShowTooltip = () => MaterialText.SetTooltip(T(preferences.UiLcarsSoundsMuted ? "UI sounds muted" : "UI sounds on")),
        };
        soundButton.Icon = preferences.UiLcarsSoundsMuted ? Dalamud.Interface.FontAwesomeIcon.VolumeMute : Dalamud.Interface.FontAwesomeIcon.VolumeUp;
        if (lcars && !owner.TitleBarButtons.Contains(soundButton)) owner.TitleBarButtons.Add(soundButton);
        else if (!lcars) owner.TitleBarButtons.Remove(soundButton);
    }
    public void Dispose() { uiSounds?.Dispose(); fonts.Dispose(); shapedText.Dispose(); foreach (var set in sets.Values) set.Dispose(); }
}
