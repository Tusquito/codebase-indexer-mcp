using CodebaseIndexer.Application.BuildDeps;

namespace CodebaseIndexer.Application.Tests.BuildDeps;

/// <summary>Port of mcp_server/tests/test_build_deps.py extractor cases.</summary>
public sealed class BuildDepExtractorTests
{
    [Theory]
    [InlineData("pom.xml")]
    [InlineData("submodule/pom.xml")]
    [InlineData("MyProject.csproj")]
    [InlineData("package.json")]
    [InlineData("build.gradle")]
    [InlineData("go.mod")]
    [InlineData("Cargo.toml")]
    [InlineData("pyproject.toml")]
    [InlineData("requirements.txt")]
    public void IsBuildManifest_known_paths(string path) =>
        Assert.True(BuildManifestDetector.IsBuildManifest(path));

    [Theory]
    [InlineData("src/Main.java")]
    [InlineData("README.md")]
    [InlineData("application.yml")]
    public void IsBuildManifest_rejects_non_manifests(string path) =>
        Assert.False(BuildManifestDetector.IsBuildManifest(path));

    [Fact]
    public void Extract_maven_pom_dependencies_and_parent()
    {
        const string pom = """
            <project>
              <parent>
                <groupId>com.example.myapp</groupId>
                <artifactId>myapp-parent</artifactId>
                <version>1.0.0</version>
              </parent>
              <dependencies>
                <dependency>
                  <groupId>com.example.contracts</groupId>
                  <artifactId>myapp-contracts-definitions</artifactId>
                  <version>2.1.0-SNAPSHOT</version>
                </dependency>
                <dependency>
                  <groupId>junit</groupId>
                  <artifactId>junit</artifactId>
                  <scope>test</scope>
                </dependency>
              </dependencies>
            </project>
            """;

        var deps = BuildDepExtractor.Extract(pom, "pom.xml");
        Assert.Equal(3, deps.Count);
        Assert.Contains(deps, d => d.Artifact == "myapp-contracts-definitions" && d.Group == "com.example.contracts");
        Assert.Contains(deps, d => d.Artifact == "junit" && d.Scope == "test");
        Assert.Contains(deps, d => d.Artifact == "myapp-parent" && d.Scope == "parent");
    }

    [Fact]
    public void Extract_nuget_package_and_project_references()
    {
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <ProjectReference Include="..\MyLib\MyLib.csproj" />
              </ItemGroup>
            </Project>
            """;

        var deps = BuildDepExtractor.Extract(csproj, "MyApp.csproj");
        Assert.Contains(deps, d => d.Artifact == "Newtonsoft.Json" && d.Version == "13.0.3");
        Assert.Contains(deps, d => d.Scope == "project" && d.Artifact == "MyLib");
    }

    [Fact]
    public void Extract_npm_dependencies()
    {
        const string pkg = """
            {
              "dependencies": { "express": "^4.18.0" },
              "devDependencies": { "jest": "^29.0.0" }
            }
            """;

        var deps = BuildDepExtractor.Extract(pkg, "package.json");
        Assert.Contains(deps, d => d.Artifact == "express" && d.Scope == "dependencies");
        Assert.Contains(deps, d => d.Artifact == "jest" && d.Scope == "devDependencies");
    }

    [Fact]
    public void Match_deps_to_collections_exact_and_fuzzy()
    {
        var deps = BuildDepExtractor.Extract(
            """
            <dependencies>
              <dependency>
                <groupId>com.example</groupId>
                <artifactId>myapp-contracts-definitions</artifactId>
              </dependency>
            </dependencies>
            """,
            "pom.xml");
        var matches = BuildDepCollectionMatcher.Match(
            deps, ["myapp-contracts", "other-svc"], selfCollection: "caller");
        Assert.Contains(matches, m => m.MatchedCollection == "myapp-contracts");
    }
}
