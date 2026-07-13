using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Configuration;
using FluentValidation.TestHelper;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for options validators.</summary>
public sealed class SettingsValidatorTests
{
    /// <summary>Valid Qdrant options pass validation.</summary>
    [Fact]
    public void Valid_qdrant_options_pass()
    {
        var validator = new QdrantOptionsValidator();
        var result = validator.TestValidate(TestSettingsFactory.CreateQdrantOptions());
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>Empty Qdrant URL fails validation.</summary>
    [Fact]
    public void Qdrant_url_fails_when_empty()
    {
        var validator = new QdrantOptionsValidator();
        var result = validator.TestValidate(TestSettingsFactory.CreateQdrantOptions(url: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    /// <summary>Empty TEI URL fails validation.</summary>
    [Fact]
    public void Tei_url_fails_when_empty()
    {
        var validator = new TeiOptionsValidator();
        var result = validator.TestValidate(TestSettingsFactory.CreateTeiOptions(url: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.Url);
    }

    /// <summary>Dense vector size must be positive.</summary>
    [Fact]
    public void Dense_vector_size_must_be_positive()
    {
        var validator = new EmbeddingOptionsValidator();
        var result = validator.TestValidate(TestSettingsFactory.CreateEmbeddingOptions(denseVectorSize: 0));
        result.ShouldHaveValidationErrorFor(x => x.DenseVectorSize);
    }
}
