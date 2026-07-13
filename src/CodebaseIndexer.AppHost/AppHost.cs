var builder = DistributedApplication.CreateBuilder(args);

var denseModel = builder.AddParameter("denseModel", "jinaai/jina-embeddings-v2-base-code");
var denseVectorSize = builder.AddParameter("denseVectorSize", "768");
var hfToken = builder.AddParameter("hfToken", string.Empty);
var teiImageName = "ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest";

var qdrant = builder.AddContainer("qdrant", "qdrant/qdrant", "v1.18.2")
    .WithHttpEndpoint(port: 6333, targetPort: 6333, name: "http")
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
    .WithEnvironment("CodebaseIndexer__QdrantUrl", qdrant.GetEndpoint("http"))
    .WithEnvironment("CodebaseIndexer__TeiUrl", tei.GetEndpoint("http"))
    .WaitFor(qdrant)
    .WaitFor(tei)
    .WithHttpEndpoint(port: 8000, name: "mcp")
    .WithEnvironment("CodebaseIndexer__DenseEmbedModel", denseModel)
    .WithEnvironment("CodebaseIndexer__DenseEmbedVectorSize", denseVectorSize)
    .WithEnvironment("CodebaseIndexer__SparseEmbedModel", "Qdrant/bm25")
    .WithEnvironment("CodebaseIndexer__HybridSearch", "true");

builder.AddDockerComposeEnvironment("compose");

builder.Build().Run();
