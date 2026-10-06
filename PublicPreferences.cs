using System.Text.Json.Nodes;
using Dalamud.Plugin;

namespace mom.PublicShell;

// Merge only presentation preferences into the shared root; private fields retain their JSON.
internal sealed class PublicPreferences
{
    private readonly string path;
    private DateTime observedWrite = DateTime.MinValue;
    internal uint Accent { get; set; } = 0xA475FF;
    internal bool Compact { get; set; }
    internal string Language { get; set; } = "en";
    internal bool UiCompactVisibleOnMainWindow { get; set; } = true;
    internal bool UiLanguageVisibleOnMainWindow { get; set; } = true;
    internal bool UiTransparencyEnabled { get; set; } = true;
    private int uiWindowOpacityPercent = 100;
    internal int UiWindowOpacityPercent { get => uiWindowOpacityPercent; set => uiWindowOpacityPercent = Math.Clamp(value, 10, 100); }
    internal bool UiAutoFade { get; set; } = true;
    private int uiFadedOpacityPercent = 50;
    internal int UiFadedOpacityPercent { get => uiFadedOpacityPercent; set => uiFadedOpacityPercent = Math.Clamp(value, 10, 100); }
    private int uiUnfocusedDelaySeconds = 10;
    internal int UiUnfocusedDelaySeconds { get => uiUnfocusedDelaySeconds; set => uiUnfocusedDelaySeconds = Math.Max(0, value); }

    internal PublicPreferences(IDalamudPluginInterface pi) { path = pi.ConfigFile.FullName; Reload(); }
    internal void Reload()
    {
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (written == observedWrite) return;
        var root = Read();
        Accent = root["UiAccentRgb"]?.GetValue<uint>() ?? 0xA475FF;
        Compact = root["UiCompact"]?.GetValue<bool>() ?? false;
        Language = root["PublicUiLanguage"]?.GetValue<string>() ?? "en";
        UiCompactVisibleOnMainWindow = root["UiCompactVisibleOnMainWindow"]?.GetValue<bool>() ?? true;
        UiLanguageVisibleOnMainWindow = root["UiLanguageVisibleOnMainWindow"]?.GetValue<bool>() ?? true;
        UiTransparencyEnabled = root["UiTransparencyEnabled"]?.GetValue<bool>() ?? true;
        UiWindowOpacityPercent = root["UiWindowOpacityPercent"]?.GetValue<int>() ?? 100;
        UiAutoFade = root["UiAutoFade"]?.GetValue<bool>() ?? true;
        UiFadedOpacityPercent = root["UiFadedOpacityPercent"]?.GetValue<int>() ?? 50;
        UiUnfocusedDelaySeconds = root["UiUnfocusedDelaySeconds"]?.GetValue<int>() ?? 10;
        observedWrite = written;
    }
    private JsonObject Read() => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("MOM configuration must be an object.")
        : new JsonObject();
    internal void Save()
    {
        var root = Read();
        root["UiAccentRgb"] = Accent; root["UiCompact"] = Compact; root["PublicUiLanguage"] = Language;
        root["UiCompactVisibleOnMainWindow"] = UiCompactVisibleOnMainWindow;
        root["UiLanguageVisibleOnMainWindow"] = UiLanguageVisibleOnMainWindow;
        root["UiTransparencyEnabled"] = UiTransparencyEnabled;
        root["UiWindowOpacityPercent"] = UiWindowOpacityPercent;
        root["UiAutoFade"] = UiAutoFade;
        root["UiFadedOpacityPercent"] = UiFadedOpacityPercent;
        root["UiUnfocusedDelaySeconds"] = UiUnfocusedDelaySeconds;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new System.Text.UTF8Encoding(false), leaveOpen: true))
            { writer.Write(root.ToJsonString(new() { WriteIndented = true })); writer.Flush(); output.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            observedWrite = File.GetLastWriteTimeUtc(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
