using CodebaseIndexer.Infrastructure.Indexing;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Callee token extraction fixtures (Python chunker Path D).</summary>
public sealed class CalleeExtractorTests
{
    [Fact]
    public void Extract_receiver_method_and_bare_calls()
    {
        const string content = """
            if (ready) {
              featureService.isEnabled("flag");
              DoWork();
              return true;
            }
            """;

        var callees = CalleeExtractor.Extract(content);
        Assert.Contains("isEnabled", callees);
        Assert.Contains("featureService.isEnabled", callees);
        Assert.Contains("DoWork", callees);
        Assert.DoesNotContain("if", callees);
        Assert.DoesNotContain("return", callees);
    }

    [Fact]
    public void Extract_dedupes_tokens()
    {
        var callees = CalleeExtractor.Extract("Foo.Bar(); Foo.Bar();");
        Assert.Equal(1, callees.Count(c => c == "Bar"));
        Assert.Equal(1, callees.Count(c => c == "Foo.Bar"));
    }
}
