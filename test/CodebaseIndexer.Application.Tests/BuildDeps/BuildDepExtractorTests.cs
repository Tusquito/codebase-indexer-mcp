using CodebaseIndexer.Application.BuildDeps;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests.BuildDeps;

/// <summary>Port of mcp_server/tests/test_build_deps.py extractor cases.</summary>
public sealed class BuildDepExtractorTests
{
    [Test]
    [Arguments("pom.xml")]
    [Arguments("submodule/pom.xml")]
    [Arguments("MyProject.csproj")]
    [Arguments("package.json")]
    [Arguments("build.gradle")]
    [Arguments("go.mod")]
    [Arguments("Cargo.toml")]
    [Arguments("pyproject.toml")]
    [Arguments("requirements.txt")]
    public async Task IsBuildManifest_known_paths(string path) =>
        await Assert.That(BuildManifestDetector.IsBuildManifest(path)).IsTrue();

    [Test]
    [Arguments("src/Main.java")]
    [Arguments("README.md")]
    [Arguments("application.yml")]
    public async Task IsBuildManifest_rejects_non_manifests(string path) =>
        await Assert.That(BuildManifestDetector.IsBuildManifest(path)).IsFalse();

    [Test]
    public async Task Extract_maven_pom_dependencies_and_parent()
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
        await Assert.That(deps.Count).IsEqualTo(3);
        await Assert.That(deps).Contains(d => d.Artifact == "myapp-contracts-definitions" && d.Group == "com.example.contracts");
        await Assert.That(deps).Contains(d => d.Artifact == "junit" && d.Scope == "test");
        await Assert.That(deps).Contains(d => d.Artifact == "myapp-parent" && d.Scope == "parent");
    }

    [Test]
    public async Task Extract_nuget_package_and_project_references()
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
        await Assert.That(deps).Contains(d => d.Artifact == "Newtonsoft.Json" && d.Version == "13.0.3");
        await Assert.That(deps).Contains(d => d.Scope == "project" && d.Artifact == "MyLib");
    }

    [Test]
    public async Task Extract_npm_dependencies()
    {
        const string pkg = """
            {
              "dependencies": { "express": "^4.18.0" },
              "devDependencies": { "jest": "^29.0.0" }
            }
            """;

        var deps = BuildDepExtractor.Extract(pkg, "package.json");
        await Assert.That(deps).Contains(d => d.Artifact == "express" && d.Scope == "dependencies");
        await Assert.That(deps).Contains(d => d.Artifact == "jest" && d.Scope == "devDependencies");
    }

    [Test]
    public async Task Match_deps_to_collections_exact_and_fuzzy()
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
        await Assert.That(matches).Contains(m => m.MatchedCollection == "myapp-contracts");
    }
}