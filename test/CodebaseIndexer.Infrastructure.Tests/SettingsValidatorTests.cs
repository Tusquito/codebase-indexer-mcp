using CodebaseIndexer.Infrastructure.Configuration;
using FluentValidation.TestHelper;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class SettingsValidatorTests
{
    private readonly SettingsValidator _validator = new();

    [Fact]
    public void Valid_settings_pass()
    {
        var result = _validator.TestValidate(CreateValidSettings());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void QdrantUrl_fails_when_empty()
    {
        var result = _validator.TestValidate(CreateValidSettings(qdrantUrl: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.QdrantUrl);
    }

    [Fact]
    public void TeiUrl_fails_when_empty()
    {
        var result = _validator.TestValidate(CreateValidSettings(teiUrl: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.TeiUrl);
    }

    [Fact]
    public void DenseEmbedVectorSize_must_be_positive()
    {
        var result = _validator.TestValidate(CreateValidSettings(denseEmbedVectorSize: 0));
        result.ShouldHaveValidationErrorFor(x => x.DenseEmbedVectorSize);
    }

    private static Settings CreateValidSettings(
        string? qdrantUrl = null,
        string? teiUrl = null,
        int? denseEmbedVectorSize = null) => new()
    {
        QdrantUrl = qdrantUrl ?? "http://localhost:6333",
        QdrantTimeoutSeconds = 30,
        QdrantCollection = "codebase",
        HybridSearch = true,
        DenseEmbedModel = "test-model",
        SparseEmbedModel = "Qdrant/bm25",
        DenseEmbedVectorSize = denseEmbedVectorSize ?? 768,
        TeiUrl = teiUrl ?? "http://localhost:8080",
        TeiEmbedBatchSize = 32,
        TeiTimeoutSeconds = 120,
        QueryInstruction = string.Empty,
        NormalizeOutput = false,
        RerankEnabled = false,
        PayloadIndexes = true,
        VectorsOnDisk = false,
        SparseOnDisk = false,
    };
}
