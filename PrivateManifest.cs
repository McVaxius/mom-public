using System;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace XA.Access;

// Shared package contract; kept identical in APM and the public hosts.
public sealed class PrivateManifest
{
    public Version Version { get; }
    public Version PublicFacingVersion { get; }
    private PrivateManifest(Version version, Version publicVersion) { Version = version; PublicFacingVersion = publicVersion; }

    private static Version ParseVersion(string? text)
    {
        if (text == null || text.Split('.').Length != 4 || !System.Version.TryParse(text, out var value)
            || new[] { value.Major, value.Minor, value.Build, value.Revision }.Any(n => n < 0 || n > 65535))
            throw new InvalidDataException("Private manifest requires four-part versions from 0 to 65535.");
        return value;
    }

    public static PrivateManifest Parse(byte[] json, string internalName)
    {
        if (json.Length == 0 || json.Length > 16384) throw new InvalidDataException("Invalid private manifest size.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1)
            || root.GetProperty("SchemaVersion").GetInt32() != 1
            || root.GetProperty("InternalName").GetString() != internalName
            || root.GetProperty("DalamudApiLevel").GetInt32() != 15)
            throw new InvalidDataException("Private manifest identity, schema, or API mismatch.");
        return new(ParseVersion(root.GetProperty("AssemblyVersion").GetString()), ParseVersion(root.GetProperty("PublicFacingVersion").GetString()));
    }

    public static byte[]? Embedded(byte[] carrier, string internalName)
    {
        using var pe = new PEReader(new MemoryStream(carrier, false));
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null) throw new InvalidDataException("Invalid private carrier.");
        var reader = pe.GetMetadataReader();
        if (reader.GetString(reader.GetAssemblyDefinition().Name) != internalName + ".Access")
            throw new InvalidDataException("Private carrier identity mismatch.");
        var resources = reader.ManifestResources.Select(reader.GetManifestResource)
            .Where(r => reader.GetString(r.Name) == internalName + ".AccessManifest").ToArray();
        if (resources.Length == 0) return null; // Legacy signed packages have no manifest.
        if (resources.Length != 1 || !resources[0].Implementation.IsNil) throw new InvalidDataException("Invalid embedded private manifest.");
        var directory = pe.PEHeaders.CorHeader.ResourcesDirectory;
        var section = pe.GetSectionData(directory.RelativeVirtualAddress);
        var offset = checked((int)resources[0].Offset);
        if (offset < 0 || offset > directory.Size - 4) throw new InvalidDataException("Invalid manifest bounds.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(section.GetContent(offset, 4).AsSpan());
        if (length <= 0 || length > 16384 || length > directory.Size - offset - 4) throw new InvalidDataException("Invalid manifest length.");
        var bytes = section.GetContent(offset + 4, length).ToArray();
        var manifest = Parse(bytes, internalName);
        if (reader.GetAssemblyDefinition().Version != manifest.Version) throw new InvalidDataException("Manifest and carrier versions differ.");
        return bytes;
    }

    // Call only alongside host-owned authentication; embedding alone is not a signature check.
    public static PrivateManifest? Validate(byte[] carrier, byte[]? json, string internalName)
    {
        var embedded = Embedded(carrier, internalName);
        if (embedded == null && json == null) return null;
        if (embedded == null || json == null || !embedded.AsSpan().SequenceEqual(json))
            throw new InvalidDataException("Private JSON must match the signed carrier exactly.");
        return Parse(json, internalName);
    }

    public static byte[] ReadFile(string path)
    {
        for (FileSystemInfo? item = new FileInfo(path); item != null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Private manifest path cannot use links.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16384) throw new InvalidDataException("Private manifest is too large.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static string? Check(PrivateManifest incoming, Version? installedVersion, Version? installedPublicVersion, bool overwrite)
    {
        if (installedVersion == null) return null;
        if (incoming.Version > installedVersion || (installedPublicVersion != null && incoming.PublicFacingVersion > installedPublicVersion)) return null;
        if (incoming.Version == installedVersion && (installedPublicVersion == null || incoming.PublicFacingVersion == installedPublicVersion))
            return overwrite ? null : "Already patched to this version";
        return $"Already patched to a newer version ({installedVersion}). Neither private nor public-facing version advances.";
    }
}
