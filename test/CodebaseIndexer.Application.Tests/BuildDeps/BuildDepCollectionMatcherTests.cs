using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Tests.BuildDeps;

/// <summary>Port of mcp_server/tests/test_build_deps.py match_deps_to_collections cases.</summary>
public sealed class BuildDepCollectionMatcherTests
{
    private static BuildDep Dep(string artifact, string group = "", string ecosystem = "maven") =>
        new(artifact, group, Ecosystem: ecosystem);

    [Fact]
    public void Match_exact_artifact_to_collection()
    {
        var deps = new[] { Dep("my-contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts", "my-engine"]);

        Assert.Single(matches);
        Assert.Equal("my-contracts", matches[0].MatchedCollection);
        Assert.Equal("exact", matches[0].MatchConfidence);
    }

    [Fact]
    public void Match_fuzzy_after_suffix_strip()
    {
        var deps = new[] { Dep("my-contracts-definitions", group: "com.example.contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts", "my-engine"]);

        Assert.Contains(matches, m => m.MatchedCollection == "my-contracts");
    }

    [Fact]
    public void Match_via_group_substring()
    {
        var deps = new[] { Dep("some-artifact", group: "com.example.my-contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        Assert.NotEmpty(matches);
    }

    [Fact]
    public void Match_excludes_self_collection()
    {
        var deps = new[] { Dep("my-service") };
        var matches = BuildDepCollectionMatcher.Match(
            deps,
            ["my-service", "my-engine"],
            selfCollection: "my-service");

        Assert.DoesNotContain(matches, m => m.MatchedCollection == "my-service");
    }

    [Fact]
    public void Match_rejects_short_names_below_fuzzy_threshold()
    {
        var deps = new[] { Dep("log") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["logback", "my-logger"]);

        Assert.Empty(matches);
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        var deps = new[] { Dep("My-Contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        Assert.Single(matches);
    }

    [Fact]
    public void Match_deduplicates_same_artifact_collection()
    {
        var deps = new[]
        {
            Dep("my-contracts-definitions", group: "com.example.contracts"),
            Dep("my-contracts-definitions", group: "com.example.contracts"),
        };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        Assert.Single(matches);
    }
}
