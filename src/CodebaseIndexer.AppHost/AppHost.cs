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

var mcp = builder.AddProject<Projects.CodebaseIndexer_Host>("mcp")
    .WithEnvironment("Qdrant__Url", qdrant.GetEndpoint("grpc"))
    .WithEnvironment("Tei__Url", tei.GetEndpoint("http"))
    .WaitFor(qdrant)
    .WaitFor(tei)
    .WithHttpEndpoint(port: 8000, name: "mcp")
    .WithEnvironment("Embedding__DenseModel", denseModel)
    .WithEnvironment("Embedding__DenseVectorSize", denseVectorSize)
    .WithEnvironment("Embedding__SparseModel", "Qdrant/bm25")
    .WithEnvironment("Embedding__HybridSearch", "true");

builder.AddDockerComposeEnvironment("compose");

builder.Build().Run();
