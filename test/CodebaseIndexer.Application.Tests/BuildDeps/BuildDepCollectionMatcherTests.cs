using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Domain.Models;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests.BuildDeps;

/// <summary>Port of mcp_server/tests/test_build_deps.py match_deps_to_collections cases.</summary>
public sealed class BuildDepCollectionMatcherTests
{
    private static BuildDep Dep(string artifact, string group = "", string ecosystem = "maven") =>
        new(artifact, group, Ecosystem: ecosystem);

    [Test]
    public async Task Match_exact_artifact_to_collection()
    {
        var deps = new[] { Dep("my-contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts", "my-engine"]);

        await Assert.That(matches).HasSingleItem();
        await Assert.That(matches[0].MatchedCollection).IsEqualTo("my-contracts");
        await Assert.That(matches[0].MatchConfidence).IsEqualTo("exact");
    }

    [Test]
    public async Task Match_fuzzy_after_suffix_strip()
    {
        var deps = new[] { Dep("my-contracts-definitions", group: "com.example.contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts", "my-engine"]);

        await Assert.That(matches).Contains(m => m.MatchedCollection == "my-contracts");
    }

    [Test]
    public async Task Match_via_group_substring()
    {
        var deps = new[] { Dep("some-artifact", group: "com.example.my-contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        await Assert.That(matches).IsNotEmpty();
    }

    [Test]
    public async Task Match_excludes_self_collection()
    {
        var deps = new[] { Dep("my-service") };
        var matches = BuildDepCollectionMatcher.Match(
            deps,
            ["my-service", "my-engine"],
            selfCollection: "my-service");

        await Assert.That(matches).DoesNotContain(m => m.MatchedCollection == "my-service");
    }

    [Test]
    public async Task Match_rejects_short_names_below_fuzzy_threshold()
    {
        var deps = new[] { Dep("log") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["logback", "my-logger"]);

        await Assert.That(matches).IsEmpty();
    }

    [Test]
    public async Task Match_is_case_insensitive()
    {
        var deps = new[] { Dep("My-Contracts") };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        await Assert.That(matches).HasSingleItem();
    }

    [Test]
    public async Task Match_deduplicates_same_artifact_collection()
    {
        var deps = new[]
        {
            Dep("my-contracts-definitions", group: "com.example.contracts"),
            Dep("my-contracts-definitions", group: "com.example.contracts"),
        };
        var matches = BuildDepCollectionMatcher.Match(deps, ["my-contracts"]);

        await Assert.That(matches).HasSingleItem();
    }
}