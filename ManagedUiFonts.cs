using AethertekUI;
using AethertekUI.Dalamud;
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
    private readonly IUiBuilder builder;
    private readonly string label;
    private IFontAtlas? atlas;
    internal MaterialTextRenderer? ShapedText { get; set; }
    private readonly string assetRoot;
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
    internal ManagedUiFonts(IUiBuilder builder, string label, MaterialTextRenderer? shapedText, string assetRoot, bool publicHost = false)
    {
        this.builder = builder;
        this.label = label;
        ShapedText = shapedText;
        this.assetRoot = assetRoot;
        this.publicHost = publicHost;
    }
    internal Exception? Error => glyphError ?? handles.Concat(bodyHandles).FirstOrDefault(h => h.LoadException != null)?.LoadException;
    internal void Prepare(string selected, IEnumerable<string> strings)
        => PrepareForDensity(selected, strings, compact);
    internal void PrepareForDensity(string selected, IEnumerable<string> strings, bool selectedCompact)
    {
        if (selected == language && selectedCompact == compact) return;
        Release();
        language = selected; compact = selectedCompact;
        required = strings.Concat(["English", "Deutsch", "Français", "Español", "Italiano", "Русский", "日本語", "한국어", "简体中文", "繁體中文", "Português (Brasil)", "Tiếng Việt", "Bahasa Indonesia", "Polski", "Türkçe", "Hindi (unavailable)", "♡", "—", "…"]).Distinct().ToArray();
        var ranges = required.Select(MaterialText.NativeGlyphText).SelectMany(text => text).Where(c => !char.IsControl(c))
            .Concat(Enumerable.Range(0x20, 0x250 - 0x20).Select(i => (char)i))
            .Concat(Enumerable.Range(0x400, 0x130).Select(i => (char)i)).ToGlyphRange();
        var created = new List<IFontHandle>();
        IFontHandle Create(float size, MaterialFontWeight weight)
        {
            var handle = MaterialFontAtlas.CreateFont(atlas!, weight, size, ranges, selected, assetRoot,
                pageSize: 2048, includeHostLanguage: true);
            created.Add(handle);
            handle.ImFontChanged += Changed;
            return handle;
        }
        try
        {
            atlas = builder.CreateFontAtlas(FontAtlasAutoRebuildMode.OnNewFrame, true, label);
            atlas.BuildStepChange += MaterialFontAtlas.ReleaseRasterizedInputs;
            using var suppress = atlas.SuppressAutoRebuild();
            handles = UiStyle.FontSizes.Select((pointSize, index) => Create(pointSize * 4 / 3, UiStyle.FontWeights[index])).ToArray();
            bodyHandles = BodySizes().Select(size => Create(size, UiStyle.FontWeights[(int)UiFontRole.Body])).ToArray();
        }
        catch (Exception error)
        {
            handles = created.ToArray();
            bodyHandles = [];
            Release();
            language = selected;
            glyphError = error;
        }
    }
    internal float[] BodySizes() => publicHost
        ? [compact ? 56f / 3 : 20f, compact ? 19.2f : 32f]
        : [compact ? 55f / 3 : 20f, compact ? 16.5f : 17.6f];
    private void Changed(IFontHandle handle, ILockedImFont font) => System.Threading.Interlocked.Increment(ref generation);
    internal unsafe bool Ready()
    {
        if (ShapedText is not { } shapedText || handles.Length != UiStyle.FontSizes.Length || bodyHandles.Length != 2
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
        statusMotion.PreDraw("MOM##FontStatus", null, null, reducedMotion: !MaterialTheme.Current.Motion.Enabled, prepareDecorations: statusDecorations.Prepare);
        bool open;
        using (MaterialSkin.BackdropStyle()) open = ImGui.Begin("MOM##FontStatus", ImGuiWindowFlags.AlwaysAutoResize);
        try
        {
            if (open)
            {
                MaterialSkin.Backdrop();
                statusDecorations.Paint();
                if (MaterialTheme.Current.IsLcars) MaterialLcars.Header("MOM");
                MaterialText.TextWrapped(message);
                if (showEnglishRecovery || useEnglish is not null)
                {
                    ImGui.BeginDisabled(useEnglish is null);
                    try { if (UiStyle.NativeButton("Use English", "Use English", default)) useEnglish?.Invoke(); }
                    finally { ImGui.EndDisabled(); }
                }
            }
        }
        finally
        {
            ImGui.End();
            statusDecorations.Paint();
            statusMotion.PostDraw();
        }
    }
    internal void Release()
    {
        if (atlas is null && language.Length == 0) return;
        if (atlas is not null) atlas.BuildStepChange -= MaterialFontAtlas.ReleaseRasterizedInputs;
        foreach (var handle in handles.Concat(bodyHandles)) { handle.ImFontChanged -= Changed; handle.Dispose(); }
        handles = [];
        bodyHandles = [];
        atlas?.Dispose();
        atlas = null;
        language = "";
        required = [];
        checkedGeneration = -1;
        glyphError = null;
        HindiAvailable = false;
    }

    public void Dispose() => Release();
}
