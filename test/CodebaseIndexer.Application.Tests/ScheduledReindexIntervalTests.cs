using CodebaseIndexer.Application.Services;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Interval parsing for scheduled reindex.</summary>
public sealed class ScheduledReindexIntervalTests
{
    [Test]
    [Arguments("6h", true)]
    [Arguments("30m", true)]
    [Arguments("90s", true)]
    [Arguments("", false)]
    [Arguments("nope", false)]
    public async Task TryParseInterval_matrix(string raw, bool expected)
    {
        var ok = ScheduledReindexIntervalHostedService.TryParseInterval(raw, out var period);
        await Assert.That(ok).IsEqualTo(expected);
        if (expected)
        {
            await Assert.That(period > TimeSpan.Zero).IsTrue();
        }
    }
}