using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for splitting source files into indexable code chunks.</summary>
public interface ICodeChunker
{
    /// <summary>Splits a source file into semantic code chunks.</summary>
    /// <param name="relPath">Repository-relative path of the file.</param>
    /// <param name="content">Full text content of the file.</param>
    /// <param name="language">Programming language of the file.</param>
    /// <param name="fileSha256">SHA-256 hash of the file content.</param>
    /// <returns>Chunks extracted from the file.</returns>
    IReadOnlyList<Chunk> ChunkFile(string relPath, string content, SourceLanguage language, string fileSha256);
}
