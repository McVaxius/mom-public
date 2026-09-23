using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace mom.PublicShell;

public static class ModulePackage
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    private const int TrailerBytes = 32 + 384 + 8;

    // Verifies the exact byte snapshot before reading embedded resources or loading code.
    public static byte[] VerifyAndDecrypt(byte[] package, string publicKeyPem, Version hostVersion)
    {
        if (package.Length < TrailerBytes + 128 || package.Length > MaximumBytes ||
            !package.AsSpan(package.Length - 8).SequenceEqual("XAZSIG01"u8))
            throw new InvalidDataException("Invalid access package.");
        var metaOffset = package.Length - TrailerBytes;
        var metadata = package.AsSpan(metaOffset, 32);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (rsa.KeySize != 3072 || !rsa.VerifyData(package.AsSpan(0, metaOffset + 32),
                package.AsSpan(metaOffset + 32, 384), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Access verification failed.");
        var schema = BinaryPrimitives.ReadInt32LittleEndian(metadata);
        var abi = BinaryPrimitives.ReadInt32LittleEndian(metadata[4..]);
        var major = BinaryPrimitives.ReadInt32LittleEndian(metadata[8..]);
        var minor = BinaryPrimitives.ReadInt32LittleEndian(metadata[12..]);
        var build = BinaryPrimitives.ReadInt32LittleEndian(metadata[16..]);
        var low = BinaryPrimitives.ReadInt32LittleEndian(metadata[20..]);
        var high = BinaryPrimitives.ReadInt32LittleEndian(metadata[24..]);
        var reserved = BinaryPrimitives.ReadInt32LittleEndian(metadata[28..]);
        // Release numbers do not determine loader compatibility. Keep the signed
        // envelope schema and ABI checks; schema 1 remains readable for migration.
        var legacy = schema == 1 && major >= 0 && minor >= 0 && build >= 0
            && low >= 1 && low <= int.MaxValue - 4 && (low - 1) % 5 == 0 && high == low + 4;
        var current = schema == 2 && major == 0 && minor == 0 && build == 0 && low == 0 && high == 0;
        if (abi != 1 || reserved != 0 || (!legacy && !current))
            throw new InvalidDataException("Invalid access format or loader ABI.");

        using var stream = new MemoryStream(package, 0, metaOffset, false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var matches = reader.ManifestResources.Select(reader.GetManifestResource)
            .Where(r => reader.GetString(r.Name) == "mom.Payload").ToArray();
        if (matches.Length != 1 || !matches[0].Implementation.IsNil || pe.PEHeaders.CorHeader == null)
            throw new InvalidDataException("Missing access payload.");
        var directory = pe.PEHeaders.CorHeader.ResourcesDirectory;
        var section = pe.GetSectionData(directory.RelativeVirtualAddress);
        var offset = checked((int)matches[0].Offset);
        if (offset < 0 || offset > directory.Size - 4) throw new InvalidDataException("Invalid resource bounds.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(section.GetContent(offset, 4).AsSpan());
        if (length < 100 || length > MaximumBytes || length > directory.Size - offset - 4)
            throw new InvalidDataException("Invalid payload size.");
        var blob = section.GetContent(offset + 4, length).AsSpan();
        if (!blob[..8].SequenceEqual("XAZENC01"u8)) throw new InvalidDataException("Invalid payload format.");
        Span<byte> key = stackalloc byte[32];
        for (var i = 0; i < key.Length; ++i) key[i] = (byte)(blob[20 + i] ^ blob[52 + i]);
        var plaintext = new byte[length - 100];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(blob.Slice(8, 12), blob.Slice(84, plaintext.Length), blob[^16..], plaintext, metadata);
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
