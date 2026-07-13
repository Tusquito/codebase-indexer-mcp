using CodebaseIndexer.Infrastructure.Configuration;
using FluentValidation.TestHelper;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class SettingsValidatorTests
{
    private readonly SettingsValidator _validator = new();

    [Fact]
    public void Valid_settings_pass()
    {
        var result = _validator.TestValidate(TestSettingsFactory.Create());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void QdrantUrl_fails_when_empty()
    {
        var result = _validator.TestValidate(TestSettingsFactory.Create(qdrantUrl: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.QdrantUrl);
    }

    [Fact]
    public void TeiUrl_fails_when_empty()
    {
        var result = _validator.TestValidate(TestSettingsFactory.Create(teiUrl: string.Empty));
        result.ShouldHaveValidationErrorFor(x => x.TeiUrl);
    }

    [Fact]
    public void DenseEmbedVectorSize_must_be_positive()
    {
        var result = _validator.TestValidate(TestSettingsFactory.Create(denseEmbedVectorSize: 0));
        result.ShouldHaveValidationErrorFor(x => x.DenseEmbedVectorSize);
    }
}
