using System.Text.Json.Serialization;

namespace CodebaseIndexer.Application.Models;

/// <summary>One search_codebase result item.</summary>
public sealed record SearchCodebaseHit(
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("symbol_type")] string SymbolType,
    [property: JsonPropertyName("start_line")] int StartLine,
    [property: JsonPropertyName("end_line")] int EndLine,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("content_truncated")] bool? ContentTruncated = null);

/// <summary>search_codebase MCP response.</summary>
public sealed record SearchCodebaseResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<SearchCodebaseHit> Results,
    [property: JsonPropertyName("collections_searched")] IReadOnlyList<string> CollectionsSearched,
    [property: JsonPropertyName("cross_references")] IReadOnlyList<CrossReferenceEntry> CrossReferences);

/// <summary>Cross-collection symbol reference entry.</summary>
public sealed record CrossReferenceEntry(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("collections")] IReadOnlyList<string> Collections,
    [property: JsonPropertyName("locations")] IReadOnlyDictionary<string, IReadOnlyList<CrossReferenceLocation>> Locations);

/// <summary>Location of a cross-referenced symbol.</summary>
public sealed record CrossReferenceLocation(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("reference_type")] string ReferenceType);

/// <summary>One search_symbols result item (no content).</summary>
public sealed record SearchSymbolsHit(
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("symbol_type")] string SymbolType,
    [property: JsonPropertyName("start_line")] int StartLine,
    [property: JsonPropertyName("end_line")] int EndLine,
    [property: JsonPropertyName("language")] string Language);

/// <summary>search_symbols MCP response.</summary>
public sealed record SearchSymbolsResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<SearchSymbolsHit> Results,
    [property: JsonPropertyName("collections_searched")] IReadOnlyList<string> CollectionsSearched);

/// <summary>get_chunk error payload.</summary>
public sealed record ChunkNotFoundResponse(
    [property: JsonPropertyName("error")] string Error);

/// <summary>get_file_outline success payload.</summary>
public sealed record FileOutlineResponse(
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("symbol_count")] int SymbolCount,
    [property: JsonPropertyName("symbols")] IReadOnlyList<FileOutlineSymbol> Symbols);

/// <summary>Outline symbol row.</summary>
public sealed record FileOutlineSymbol(
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("symbol_type")] string SymbolType,
    [property: JsonPropertyName("start_line")] int StartLine,
    [property: JsonPropertyName("end_line")] int EndLine,
    [property: JsonPropertyName("language")] string Language);

/// <summary>get_file_outline error payload.</summary>
public sealed record FileOutlineErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("hint")] string Hint);

/// <summary>get_collection_summary success payload (no build_dependencies — Phase 4).</summary>
public sealed record CollectionSummaryResponse(
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("total_files")] int TotalFiles,
    [property: JsonPropertyName("total_chunks")] int TotalChunks,
    [property: JsonPropertyName("files_by_language")] IReadOnlyDictionary<string, int> FilesByLanguage,
    [property: JsonPropertyName("symbols_by_type")] IReadOnlyDictionary<string, int> SymbolsByType,
    [property: JsonPropertyName("directory_tree")] IReadOnlyList<string> DirectoryTree,
    [property: JsonPropertyName("top_chunked_files")] IReadOnlyList<TopChunkedFile> TopChunkedFiles);

/// <summary>Top chunked file row.</summary>
public sealed record TopChunkedFile(
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("chunk_count")] int ChunkCount);

/// <summary>get_collection_summary error payload.</summary>
public sealed record CollectionSummaryErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("hint")] string Hint);

/// <summary>list_collections row.</summary>
public sealed record CollectionListItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("vector_count")] long VectorCount,
    [property: JsonPropertyName("disk_size_mb")] double DiskSizeMb,
    [property: JsonPropertyName("dense_embed_model")] string DenseEmbedModel,
    [property: JsonPropertyName("sparse_embed_model")] string SparseEmbedModel,
    [property: JsonPropertyName("hybrid")] bool Hybrid,
    [property: JsonPropertyName("rerank_enabled")] bool RerankEnabled,
    [property: JsonPropertyName("colbert_embed_model")] string? ColbertEmbedModel);
