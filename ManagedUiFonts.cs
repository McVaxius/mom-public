using Dalamud;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

#if MOM_PRIVATE_UI
namespace mom.PrivateUi;
#else
namespace mom.PublicShell;
#endif

internal sealed class ManagedUiFonts : IDisposable
{
    private readonly IFontAtlas atlas;
    private readonly MaterialTextRenderer shapedText;
    private static readonly MaterialWindowFold statusMotion = new();
    private static readonly MaterialWindowDecorations statusDecorations = new();
    private IFontHandle[] handles = [];
    private IFontHandle[] bodyHandles = [];
    private readonly bool publicHost;
    private bool compact;
    private int activeBody = -1;
    private float bodyMultiplier = 1;
    private bool usingBodyFont;
    private string language = "";
    private string[] required = [];
    private int generation;
    private int checkedGeneration = -1;
    private Exception? glyphError;
    internal bool HindiAvailable { get; private set; }
    internal ManagedUiFonts(IUiBuilder builder, string label, MaterialTextRenderer shapedText, bool publicHost = false)
    { this.shapedText = shapedText; this.publicHost = publicHost; atlas = builder.CreateFontAtlas(FontAtlasAutoRebuildMode.Async, true, label); }
    internal Exception? Error => glyphError ?? handles.Concat(bodyHandles).FirstOrDefault(h => h.LoadException != null)?.LoadException;
    internal void Prepare(string selected, IEnumerable<string> strings)
        => PrepareForDensity(selected, strings, compact);
    internal void PrepareForDensity(string selected, IEnumerable<string> strings, bool selectedCompact)
    {
        if (selected == language && selectedCompact == compact) return;
        using var suppress = atlas.SuppressAutoRebuild();
        foreach (var handle in handles.Concat(bodyHandles)) { handle.ImFontChanged -= Changed; handle.Dispose(); }
        language = selected; compact = selectedCompact;
        required = strings.Concat(["English", "Deutsch", "Français", "Español", "Italiano", "Русский", "日本語", "한국어", "简体中文", "繁體中文", "Português (Brasil)", "Tiếng Việt", "Bahasa Indonesia", "Polski", "Türkçe", "Hindi (unavailable)", "♡", "—", "…"]).Distinct().ToArray();
        var ranges = required.Select(MaterialText.NativeGlyphText).SelectMany(text => text).Where(c => !char.IsControl(c))
            .Concat(Enumerable.Range(0x20, 0x250 - 0x20).Select(i => (char)i))
            .Concat(Enumerable.Range(0x400, 0x130).Select(i => (char)i)).ToGlyphRange();
        glyphError = null; checkedGeneration = -1;
        HindiAvailable = false;
        IFontHandle Create(float size, string file) => atlas.NewDelegateFontHandle(step => step.OnPreBuild(build =>
        {
            build.NewImAtlas.TexDesiredWidth = 4096;
            build.NewImAtlas.TexDesiredHeight = 4096;
            build.Font = build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file),
                new SafeFontConfig { SizePx = size, GlyphRanges = ranges });
            build.AddFontFromFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisym.ttf"),
                new SafeFontConfig { SizePx = size, MergeFont = build.Font, GlyphRanges = ranges });
            // Verified bundled TTC faces: JP=0, KR=1, SC=2, TC=3.
            build.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular,
                new SafeFontConfig { SizePx = size, MergeFont = build.Font, GlyphRanges = ranges,
                    FontNo = selected switch { "ko" => 1, "zh-Hans" or "zh-CN" => 2, "zh-Hant" or "zh-TW" => 3, _ => 0 } });
            build.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = size, MergeFont = build.Font });
            build.AddGameSymbol(new SafeFontConfig { SizePx = size, MergeFont = build.Font });
        }));
        handles = UiStyle.FontSizes.Select((pointSize, index) => Create(pointSize * 4 / 3, UiStyle.FontFiles[index])).ToArray();
        bodyHandles = BodySizes().Select(size => Create(size, UiStyle.FontFiles[(int)UiFontRole.Body])).ToArray();
        foreach (var handle in handles.Concat(bodyHandles)) handle.ImFontChanged += Changed;
    }
    internal float[] BodySizes() => publicHost
        ? [compact ? 56f / 3 : 20f, compact ? 19.2f : 32f]
        : [compact ? 55f / 3 : 20f, compact ? 16.5f : 17.6f];
    private void Changed(IFontHandle handle, ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal unsafe bool Ready()
    {
        if (handles.Length != UiStyle.FontSizes.Length || bodyHandles.Length != 2
            || handles.Concat(bodyHandles).Any(h => !h.Available || h.LoadException != null) || glyphError != null) return false;
        var current = System.Threading.Volatile.Read(ref generation);
        if (current == checkedGeneration) return true;
        try
        {
            var allHandles = handles.Concat(bodyHandles).ToArray();
            var sizes = UiStyle.FontSizes.Select(size => size * 4 / 3).Concat(BodySizes()).ToArray();
            HindiAvailable = sizes.All(size => shapedText.TryCheckGlyphs(["हिन्दी"], size * ImGui.GetIO().FontGlobalScale, out _));
            for (var index = 0; index < allHandles.Length; index++)
            {
                shapedText.CheckGlyphs(required, sizes[index] * ImGui.GetIO().FontGlobalScale);
                using var font = allHandles[index].Lock();
                foreach (var character in required.Select(MaterialText.NativeGlyphText).SelectMany(text => text).Where(c => !char.IsControl(c)).Distinct())
                    if (ImGui.FindGlyphNoFallback(font.ImFont, character).Handle == null)
                        throw new InvalidOperationException("Required MOM UI glyph missing: U+" + ((int)character).ToString("X4") + " in font " + index);
            }
            checkedGeneration = current;
            return true;
        }
        catch (Exception error) { glyphError = error; return false; }
    }
    internal IDisposable Push(UiFontRole role)
    {
        if (!Ready()) throw new InvalidOperationException("MOM UI fonts are not ready.", Error);
        if (activeBody < 0) return handles[(int)role].Push();
        var scope = new FontScope(this, role == UiFontRole.Body ? bodyHandles[activeBody].Push() : handles[(int)role].Push());
        if (role == UiFontRole.Body && !usingBodyFont) ImGui.SetWindowFontScale(scope.Scale / bodyMultiplier);
        else if (role != UiFontRole.Body && usingBodyFont) ImGui.SetWindowFontScale(scope.Scale * bodyMultiplier);
        usingBodyFont = role == UiFontRole.Body;
        return scope;
    }
    internal IDisposable PushWindowBody(bool secondary, float multiplier)
    {
        if (!Ready()) throw new InvalidOperationException("MOM UI fonts are not ready.", Error);
        var index = secondary ? 1 : 0;
        var scope = new FontScope(this, bodyHandles[index].Push());
        activeBody = index; bodyMultiplier = multiplier; usingBodyFont = true;
        return scope;
    }
    private sealed class FontScope : IDisposable
    {
        private readonly ManagedUiFonts owner;
        private readonly IDisposable font;
        private readonly int body;
        private readonly float multiplier;
        private readonly bool wasBody;
        internal float Scale { get; }
        internal FontScope(ManagedUiFonts owner, IDisposable font)
        {
            this.owner = owner; this.font = font; body = owner.activeBody;
            multiplier = owner.bodyMultiplier; wasBody = owner.usingBodyFont;
            Scale = ImGuiP.GetCurrentWindow().FontWindowScale;
        }
        public void Dispose()
        {
            font.Dispose(); ImGui.SetWindowFontScale(Scale);
            owner.activeBody = body; owner.bodyMultiplier = multiplier; owner.usingBodyFont = wasBody;
        }
    }
    internal static void DrawStatus(string message) => DrawStatusWithRecovery(message, null);
    internal static void DrawStatusWithRecovery(string message, Action? useEnglish, bool showEnglishRecovery = false)
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(450 * ImGui.GetIO().FontGlobalScale, 0));
        statusMotion.PreDraw("MOM##FontStatus", null, null, reducedMotion: false, prepareDecorations: statusDecorations.Prepare);
        if (ImGui.Begin("MOM##FontStatus", ImGuiWindowFlags.AlwaysAutoResize))
        {
            statusDecorations.Paint();
            MaterialText.TextWrapped(message);
            if (showEnglishRecovery || useEnglish is not null)
            {
                ImGui.BeginDisabled(useEnglish is null);
                try { if (ImGui.Button("Use English")) useEnglish?.Invoke(); }
                finally { ImGui.EndDisabled(); }
            }
        }
        ImGui.End();
        statusDecorations.Paint();
        statusMotion.PostDraw();
    }
    public void Dispose()
    {
        foreach (var handle in handles.Concat(bodyHandles)) { handle.ImFontChanged -= Changed; handle.Dispose(); }
        handles = []; bodyHandles = []; atlas.Dispose();
    }
}
