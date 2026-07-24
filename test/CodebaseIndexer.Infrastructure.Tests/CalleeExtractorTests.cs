using CodebaseIndexer.Infrastructure.Indexing;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Callee token extraction fixtures (Python chunker Path D).</summary>
public sealed class CalleeExtractorTests
{
    [Test]
    public async Task Extract_receiver_method_and_bare_calls()
    {
        const string content = """
            if (ready) {
              featureService.isEnabled("flag");
              DoWork();
              return true;
            }
            """;

        var callees = CalleeExtractor.Extract(content);
        await Assert.That(callees).Contains("isEnabled");
        await Assert.That(callees).Contains("featureService.isEnabled");
        await Assert.That(callees).Contains("DoWork");
        await Assert.That(callees).DoesNotContain("if");
        await Assert.That(callees).DoesNotContain("return");
    }

    [Test]
    public async Task Extract_dedupes_tokens()
    {
        var callees = CalleeExtractor.Extract("Foo.Bar(); Foo.Bar();");
        await Assert.That(callees.Count(c => c == "Bar")).IsEqualTo(1);
        await Assert.That(callees.Count(c => c == "Foo.Bar")).IsEqualTo(1);
    }
}