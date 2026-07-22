using System.Text.Json.Serialization;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>find_cross_references success payload.</summary>
public sealed record CrossReferenceResponse(
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("member")] string? Member,
    [property: JsonPropertyName("receiver")] string? Receiver,
    [property: JsonPropertyName("collection_count")] int CollectionCount,
    [property: JsonPropertyName("found_in")] IReadOnlyDictionary<string, IReadOnlyList<CrossReferenceHit>> FoundIn,
    [property: JsonPropertyName("links")] IReadOnlyList<CrossReferenceLink> Links);

/// <summary>One find_cross_references hit (collection key removed; grouped under found_in).</summary>
public sealed record CrossReferenceHit(
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("symbol_type")] SymbolType SymbolType,
    [property: JsonPropertyName("start_line")] int StartLine,
    [property: JsonPropertyName("end_line")] int EndLine,
    [property: JsonPropertyName("language")] SourceLanguage Language,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("match_type")] string MatchType,
    [property: JsonPropertyName("reference_type")] string ReferenceType);

/// <summary>Cross-collection link summary entry.</summary>
public sealed record CrossReferenceLink(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("from")] CrossReferenceLinkEnd From,
    [property: JsonPropertyName("to")] CrossReferenceLinkEnd To,
    [property: JsonPropertyName("matched_paths")] IReadOnlyList<string>? MatchedPaths = null);

/// <summary>One end of a cross-reference link.</summary>
public sealed record CrossReferenceLinkEnd(
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("reference_type")] string? ReferenceType = null,
    [property: JsonPropertyName("symbol")] string? Symbol = null);

/// <summary>map_service_dependencies response.</summary>
public sealed record ServiceMapResponse(
    [property: JsonPropertyName("collections_analyzed")] IReadOnlyList<string> CollectionsAnalyzed,
    [property: JsonPropertyName("services")] IReadOnlyDictionary<string, ServiceMapServiceSummary> Services,
    [property: JsonPropertyName("dependency_graph")] IReadOnlyDictionary<string, IReadOnlyList<string>> DependencyGraph,
    [property: JsonPropertyName("edges")] IReadOnlyList<ServiceMapEdge> Edges,
    [property: JsonPropertyName("summary")] ServiceMapSummary Summary);

/// <summary>Per-service discovery counts.</summary>
public sealed record ServiceMapServiceSummary(
    [property: JsonPropertyName("endpoints_found")] int EndpointsFound,
    [property: JsonPropertyName("http_callers_found")] int HttpCallersFound,
    [property: JsonPropertyName("configs_found")] int ConfigsFound,
    [property: JsonPropertyName("build_manifests_found")] int BuildManifestsFound,
    [property: JsonPropertyName("build_deps_found")] int BuildDepsFound,
    [property: JsonPropertyName("calls")] IReadOnlyList<string> Calls,
    [property: JsonPropertyName("called_by")] IReadOnlyList<string> CalledBy);

/// <summary>One dependency edge.</summary>
public sealed record ServiceMapEdge(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("from_service")] string FromService,
    [property: JsonPropertyName("from_file")] string FromFile,
    [property: JsonPropertyName("from_symbol")] string? FromSymbol,
    [property: JsonPropertyName("to_service")] string ToService,
    [property: JsonPropertyName("to_file")] string? ToFile,
    [property: JsonPropertyName("to_symbol")] string? ToSymbol,
    [property: JsonPropertyName("matched_routes")] IReadOnlyList<string>? MatchedRoutes = null,
    [property: JsonPropertyName("base_urls")] IReadOnlyList<string>? BaseUrls = null,
    [property: JsonPropertyName("artifact")] string? Artifact = null,
    [property: JsonPropertyName("group")] string? Group = null,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("scope")] string? Scope = null,
    [property: JsonPropertyName("ecosystem")] string? Ecosystem = null,
    [property: JsonPropertyName("match_confidence")] string? MatchConfidence = null);

/// <summary>Aggregate counts for service-map.</summary>
public sealed record ServiceMapSummary(
    [property: JsonPropertyName("total_endpoints")] int TotalEndpoints,
    [property: JsonPropertyName("total_callers")] int TotalCallers,
    [property: JsonPropertyName("total_configs")] int TotalConfigs,
    [property: JsonPropertyName("total_build_deps")] int TotalBuildDeps,
    [property: JsonPropertyName("total_edges")] int TotalEdges);

/// <summary>recommend_code / find_outlier_chunks result item.</summary>
public sealed record DiscoveryHit(
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("rel_path")] string RelPath,
    [property: JsonPropertyName("symbol_name")] string? SymbolName,
    [property: JsonPropertyName("symbol_type")] SymbolType SymbolType,
    [property: JsonPropertyName("start_line")] int StartLine,
    [property: JsonPropertyName("end_line")] int EndLine,
    [property: JsonPropertyName("language")] SourceLanguage Language,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("content_truncated")] bool? ContentTruncated = null,
    [property: JsonPropertyName("similarity_to_context")] double? SimilarityToContext = null);

/// <summary>recommend_code response.</summary>
public sealed record RecommendCodeResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<DiscoveryHit> Results,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("positive_examples")] int PositiveExamples,
    [property: JsonPropertyName("negative_examples")] int NegativeExamples);

/// <summary>find_outlier_chunks response.</summary>
public sealed record OutlierChunksResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<DiscoveryHit> Results,
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("context_examples")] int ContextExamples,
    [property: JsonPropertyName("max_similarity")] float MaxSimilarity);

/// <summary>One build dependency reported by get_collection_summary.</summary>
public sealed record CollectionBuildDependency(
    [property: JsonPropertyName("artifact")] string Artifact,
    [property: JsonPropertyName("group")] string Group,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("ecosystem")] string Ecosystem,
    [property: JsonPropertyName("matched_collection")] string MatchedCollection,
    [property: JsonPropertyName("match_confidence")] string MatchConfidence,
    [property: JsonPropertyName("declared_in")] string DeclaredIn);
