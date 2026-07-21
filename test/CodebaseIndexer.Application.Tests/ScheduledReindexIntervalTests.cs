using CodebaseIndexer.Application.Services;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Interval parsing for scheduled reindex.</summary>
public sealed class ScheduledReindexIntervalTests
{
    [Theory]
    [InlineData("6h", true)]
    [InlineData("30m", true)]
    [InlineData("90s", true)]
    [InlineData("", false)]
    [InlineData("nope", false)]
    public void TryParseInterval_matrix(string raw, bool expected)
    {
        var ok = ScheduledReindexIntervalHostedService.TryParseInterval(raw, out var period);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.True(period > TimeSpan.Zero);
        }
    }
}
