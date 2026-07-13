using System.Security.Cryptography;
using System.Text;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Stable identifier for a code chunk derived from its path and start line.</summary>
/// <param name="Value">Hex-encoded identifier value.</param>
public readonly record struct ChunkId(string Value)
{
    /// <summary>Hex-encoded identifier value.</summary>
    public string Value { get; init; } = Value;

    /// <summary>Creates a chunk identifier from a relative path and start line.</summary>
    /// <param name="relPath">Repository-relative path of the source file.</param>
    /// <param name="startLine">One-based start line of the chunk.</param>
    /// <returns>A deterministic <see cref="ChunkId"/> for the given location.</returns>
    public static ChunkId FromPathAndLine(string relPath, int startLine)
    {
        var payload = string.Concat(relPath, ":", startLine.ToString());
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(payload)];
        var written = Encoding.UTF8.GetBytes(payload, utf8);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8[..written], hash);
        return new(Convert.ToHexStringLower(hash));
    }
}
