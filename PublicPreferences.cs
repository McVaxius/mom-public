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
    internal PublicPreferences(IDalamudPluginInterface pi) { path = pi.ConfigFile.FullName; Reload(); }
    internal void Reload()
    {
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (written == observedWrite) return;
        var root = Read();
        Accent = root["UiAccentRgb"]?.GetValue<uint>() ?? 0xA475FF;
        Compact = root["UiCompact"]?.GetValue<bool>() ?? false;
        Language = root["PublicUiLanguage"]?.GetValue<string>() ?? "en";
        observedWrite = written;
    }
    private JsonObject Read() => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("MOM configuration must be an object.")
        : new JsonObject();
    internal void Save()
    {
        var root = Read();
        root["UiAccentRgb"] = Accent; root["UiCompact"] = Compact; root["PublicUiLanguage"] = Language;
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
