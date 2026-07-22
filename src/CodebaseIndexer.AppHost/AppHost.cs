using System.Runtime.InteropServices;

var builder = DistributedApplication.CreateBuilder(args);

var denseModel = builder.AddParameter("denseModel", "jinaai/jina-embeddings-v2-base-code");
var denseVectorSize = builder.AddParameter("denseVectorSize", "768");
var hfToken = builder.AddParameter("hfToken", string.Empty);
// Mirror scripts/compose_files.py tei_image_default (ADR 0028): TEI_IMAGE override, else arch default.
var teiImageName = Environment.GetEnvironmentVariable("TEI_IMAGE");
if (string.IsNullOrWhiteSpace(teiImageName))
{
    teiImageName = RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest"
        : "ghcr.io/huggingface/text-embeddings-inference:cpu-1.9";
}

var accelerator = Environment.GetEnvironmentVariable("ACCELERATOR") ?? "gpu";
var rerankEnabledEnv = Environment.GetEnvironmentVariable("Embedding__RerankEnabled")
    ?? Environment.GetEnvironmentVariable("RERANK_ENABLED")
    ?? "false";
var rerankEnabled = string.Equals(rerankEnabledEnv, "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(rerankEnabledEnv, "1", StringComparison.OrdinalIgnoreCase);
var colbertBackend = Environment.GetEnvironmentVariable("Colbert__EmbedBackend")
    ?? Environment.GetEnvironmentVariable("COLBERT_EMBED_BACKEND")
    ?? (rerankEnabled ? "remote" : "onnx");
// Always publish ColBERT in Aspire compose (opt-in via Embedding__RerankEnabled).
var includeColbert = true;

// REST :6333 for health/metrics; gRPC :6334 for Qdrant.Client (PROTOCOL_ERROR if REST used as gRPC).
var qdrant = builder.AddContainer("qdrant", "qdrant/qdrant", "v1.18.2")
    .WithHttpEndpoint(port: 6333, targetPort: 6333, name: "http")
    .WithEndpoint(port: 6334, targetPort: 6334, name: "grpc", scheme: "http")
    .WithEnvironment("QDRANT__SERVICE__GRPC_PORT", "6334")
    .WithVolume("qdrant_data", "/qdrant/storage")
    .WithHttpHealthCheck("/healthz");

var tei = builder.AddContainer("tei", teiImageName)
    .WithHttpEndpoint(port: 8080, targetPort: 80, name: "http")
    .WithEnvironment("MODEL_ID", denseModel)
    .WithEnvironment("HF_TOKEN", hfToken)
    .WithVolume("tei_data", "/data")
    .WithArgs("--port", "80", "--max-batch-tokens", "1024")
    .WithHttpHealthCheck("/health");

var graphEnabledEnv = Environment.GetEnvironmentVariable("GRAPH_ENABLED")
    ?? Environment.GetEnvironmentVariable("Graph__Enabled");
var graphEnabled = string.Equals(graphEnabledEnv, "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(graphEnabledEnv, "1", StringComparison.OrdinalIgnoreCase);
var neo4jPassword = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")
    ?? Environment.GetEnvironmentVariable("Graph__Neo4jPassword")
    ?? string.Empty;

IResourceBuilder<ContainerResource>? neo4j = null;
if (graphEnabled)
{
    neo4j = builder.AddContainer("neo4j", "neo4j", "5.26.28-community")
        .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
        .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt", scheme: "bolt")
        .WithEnvironment("NEO4J_AUTH", $"neo4j/{neo4jPassword}")
        .WithEnvironment("NEO4J_server_memory_heap_initial__size", "512m")
        .WithEnvironment("NEO4J_server_memory_heap_max__size", "1g")
        .WithEnvironment("NEO4J_server_memory_pagecache_size", "512m")
        .WithVolume("neo4j_data", "/data")
        .WithHttpHealthCheck("/");
}

IResourceBuilder<ProjectResource>? colbert = null;
if (includeColbert)
{
    colbert = builder.AddProject<Projects.CodebaseIndexer_ColbertWorker>("colbert")
        .WithHttpEndpoint(port: 8082, name: "http")
        .WithEnvironment("Embedding__CachePath", "/root/.cache/fastembed")
        .WithEnvironment("Colbert__EmbedModel", "colbert-ir/colbertv2.0")
        .WithEnvironment("Colbert__EmbedBackend", "onnx")
        .WithEnvironment(
            "Colbert__UseCuda",
            string.Equals(accelerator, "gpu", StringComparison.OrdinalIgnoreCase) ? "true" : "false");
}

var mcp = builder.AddProject<Projects.CodebaseIndexer_Host>("mcp")
    .WithEnvironment("Qdrant__Url", qdrant.GetEndpoint("grpc"))
    .WithEnvironment("Tei__Url", tei.GetEndpoint("http"))
    .WaitFor(qdrant)
    .WaitFor(tei)
    .WithHttpEndpoint(port: 8000, name: "mcp")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Embedding__DenseModel", denseModel)
    .WithEnvironment("Embedding__DenseVectorSize", denseVectorSize)
    .WithEnvironment("Embedding__SparseModel", "Qdrant/bm25")
    .WithEnvironment("Embedding__HybridSearch", "true")
    .WithEnvironment("Embedding__RerankEnabled", rerankEnabled ? "true" : "false")
    .WithEnvironment("Colbert__EmbedBackend", colbertBackend);

if (colbert is not null)
{
    mcp = mcp
        .WithEnvironment("Colbert__Url", colbert.GetEndpoint("http"))
        .WaitFor(colbert);
}

if (neo4j is not null)
{
    mcp = mcp
        .WithEnvironment("Graph__Enabled", "true")
        .WithEnvironment("Graph__Neo4jUri", "bolt://neo4j:7687")
        .WithEnvironment("Graph__Neo4jUser", "neo4j")
        .WithEnvironment("Graph__Neo4jPassword", neo4jPassword)
        .WithEnvironment("Graph__Neo4jDatabase", "neo4j")
        .WaitFor(neo4j);
}

builder.AddDockerComposeEnvironment("compose");

builder.Build().Run();
