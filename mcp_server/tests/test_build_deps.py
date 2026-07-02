# tests/test_build_deps.py
"""Tests for codebase_indexer.tools.build_deps."""


from codebase_indexer.tools.build_deps import (
    BuildDep,
    extract_build_deps,
    is_build_manifest,
    match_deps_to_collections,
)


# ---------------------------------------------------------------------------
# is_build_manifest
# ---------------------------------------------------------------------------

class TestIsBuildManifest:
    def test_pom_xml(self):
        assert is_build_manifest("pom.xml")
        assert is_build_manifest("submodule/pom.xml")

    def test_csproj(self):
        assert is_build_manifest("MyProject.csproj")
        assert is_build_manifest("src/MyApp/MyApp.fsproj")
        assert is_build_manifest("lib/MyLib.vbproj")

    def test_package_json(self):
        assert is_build_manifest("package.json")
        assert is_build_manifest("frontend/package.json")

    def test_gradle(self):
        assert is_build_manifest("build.gradle")
        assert is_build_manifest("build.gradle.kts")
        assert is_build_manifest("settings.gradle")

    def test_go_mod(self):
        assert is_build_manifest("go.mod")
        assert is_build_manifest("cmd/server/go.mod")

    def test_cargo(self):
        assert is_build_manifest("Cargo.toml")

    def test_python(self):
        assert is_build_manifest("pyproject.toml")
        assert is_build_manifest("requirements.txt")
        assert is_build_manifest("setup.cfg")

    def test_non_manifest(self):
        assert not is_build_manifest("src/Main.java")
        assert not is_build_manifest("README.md")
        assert not is_build_manifest("application.yml")
        assert not is_build_manifest("Dockerfile")


# ---------------------------------------------------------------------------
# Maven (pom.xml)
# ---------------------------------------------------------------------------

class TestMavenExtractor:
    POM = """
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
          <groupId>org.springframework.boot</groupId>
          <artifactId>spring-boot-starter-web</artifactId>
          <scope>compile</scope>
        </dependency>
        <dependency>
          <groupId>junit</groupId>
          <artifactId>junit</artifactId>
          <scope>test</scope>
        </dependency>
      </dependencies>
    </project>
    """

    def test_dependency_count(self):
        deps = extract_build_deps(self.POM, "pom.xml")
        # 3 <dependency> blocks + 1 <parent>
        assert len(deps) == 4

    def test_artifact_names(self):
        deps = extract_build_deps(self.POM, "pom.xml")
        artifacts = {d.artifact for d in deps}
        assert "myapp-contracts-definitions" in artifacts
        assert "spring-boot-starter-web" in artifacts
        assert "junit" in artifacts
        assert "myapp-parent" in artifacts  # parent

    def test_group_and_version(self):
        deps = extract_build_deps(self.POM, "pom.xml")
        contract_dep = next(d for d in deps if d.artifact == "myapp-contracts-definitions")
        assert contract_dep.group == "com.example.contracts"
        assert contract_dep.version == "2.1.0-SNAPSHOT"
        assert contract_dep.ecosystem == "maven"

    def test_scope_extraction(self):
        deps = extract_build_deps(self.POM, "pom.xml")
        test_dep = next(d for d in deps if d.artifact == "junit")
        assert test_dep.scope == "test"

    def test_parent_scope(self):
        deps = extract_build_deps(self.POM, "pom.xml")
        parent = next(d for d in deps if d.artifact == "myapp-parent")
        assert parent.scope == "parent"


# ---------------------------------------------------------------------------
# NuGet (.csproj)
# ---------------------------------------------------------------------------

class TestNuGetExtractor:
    CSPROJ = """
    <Project Sdk="Microsoft.NET.Sdk">
      <ItemGroup>
        <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
        <PackageReference Include="Microsoft.Extensions.Logging" Version="7.0.0"/>
        <ProjectReference Include="..\\MyLib\\MyLib.csproj" />
      </ItemGroup>
    </Project>
    """

    def test_package_references(self):
        deps = extract_build_deps(self.CSPROJ, "MyApp.csproj")
        artifacts = {d.artifact for d in deps}
        assert "Newtonsoft.Json" in artifacts
        assert "Microsoft.Extensions.Logging" in artifacts

    def test_project_reference(self):
        deps = extract_build_deps(self.CSPROJ, "MyApp.csproj")
        proj_deps = [d for d in deps if d.scope == "project"]
        assert len(proj_deps) == 1
        assert proj_deps[0].artifact == "MyLib"

    def test_version(self):
        deps = extract_build_deps(self.CSPROJ, "MyApp.csproj")
        nj = next(d for d in deps if d.artifact == "Newtonsoft.Json")
        assert nj.version == "13.0.3"
        assert nj.ecosystem == "nuget"


# ---------------------------------------------------------------------------
# npm (package.json)
# ---------------------------------------------------------------------------

class TestNpmExtractor:
    PKG = """
    {
      "name": "my-app",
      "dependencies": {
        "express": "^4.18.0",
        "axios": "^1.6.0"
      },
      "devDependencies": {
        "jest": "^29.0.0"
      }
    }
    """

    def test_dependencies(self):
        deps = extract_build_deps(self.PKG, "package.json")
        artifacts = {d.artifact for d in deps}
        assert "express" in artifacts
        assert "axios" in artifacts
        assert "jest" in artifacts

    def test_scope(self):
        deps = extract_build_deps(self.PKG, "package.json")
        jest = next(d for d in deps if d.artifact == "jest")
        assert jest.scope == "devDependencies"
        express = next(d for d in deps if d.artifact == "express")
        assert express.scope == "dependencies"
        assert express.ecosystem == "npm"


# ---------------------------------------------------------------------------
# Gradle (build.gradle)
# ---------------------------------------------------------------------------

class TestGradleExtractor:
    BUILD = """
    dependencies {
        implementation 'org.springframework.boot:spring-boot-starter-web:3.0.0'
        api 'com.google.guava:guava:32.0.1-jre'
        testImplementation 'junit:junit:4.13.2'
        implementation(project(':my-lib'))
    }
    """

    def test_external_deps(self):
        deps = extract_build_deps(self.BUILD, "build.gradle")
        artifacts = {d.artifact for d in deps}
        assert "spring-boot-starter-web" in artifacts
        assert "guava" in artifacts
        assert "junit" in artifacts

    def test_project_reference(self):
        deps = extract_build_deps(self.BUILD, "build.gradle")
        proj = [d for d in deps if d.scope == "project"]
        assert any(d.artifact == "my-lib" for d in proj)

    def test_ecosystem(self):
        deps = extract_build_deps(self.BUILD, "build.gradle")
        assert all(d.ecosystem == "gradle" for d in deps)


# ---------------------------------------------------------------------------
# Go (go.mod)
# ---------------------------------------------------------------------------

class TestGoExtractor:
    GOMOD = """
    module github.com/myorg/myservice

    go 1.21

    require (
        github.com/gin-gonic/gin v1.9.1
        github.com/stretchr/testify v1.8.4
    )

    require golang.org/x/net v0.14.0
    """

    def test_require_block(self):
        deps = extract_build_deps(self.GOMOD, "go.mod")
        artifacts = {d.artifact for d in deps}
        assert "gin" in artifacts
        assert "testify" in artifacts
        assert "net" in artifacts

    def test_group_is_module_path(self):
        deps = extract_build_deps(self.GOMOD, "go.mod")
        gin = next(d for d in deps if d.artifact == "gin")
        assert gin.group == "github.com/gin-gonic/gin"
        assert gin.version == "v1.9.1"
        assert gin.ecosystem == "go"


# ---------------------------------------------------------------------------
# Cargo (Cargo.toml)
# ---------------------------------------------------------------------------

class TestCargoExtractor:
    CARGO = """
    [package]
    name = "my-crate"
    version = "0.1.0"

    [dependencies]
    serde = "1.0"
    tokio = { version = "1", features = ["full"] }

    [dev-dependencies]
    mockall = "0.11"
    """

    def test_dependencies(self):
        deps = extract_build_deps(self.CARGO, "Cargo.toml")
        artifacts = {d.artifact for d in deps}
        assert "serde" in artifacts
        assert "mockall" in artifacts

    def test_dev_scope(self):
        deps = extract_build_deps(self.CARGO, "Cargo.toml")
        mockall = next(d for d in deps if d.artifact == "mockall")
        assert mockall.scope == "dev"
        assert mockall.ecosystem == "cargo"


# ---------------------------------------------------------------------------
# Python (pyproject.toml / requirements.txt)
# ---------------------------------------------------------------------------

class TestPythonExtractor:
    PYPROJECT = """
    [project]
    name = "myservice"
    dependencies = [
        "fastapi>=0.100.0",
        "pydantic>=2.0",
        "httpx",
    ]
    """

    REQUIREMENTS = """
    # Core deps
    flask==3.0.0
    sqlalchemy>=2.0
    -r base-requirements.txt
    """

    def test_pyproject(self):
        deps = extract_build_deps(self.PYPROJECT, "pyproject.toml")
        artifacts = {d.artifact for d in deps}
        assert "fastapi" in artifacts
        assert "pydantic" in artifacts
        assert "httpx" in artifacts

    def test_requirements_txt(self):
        deps = extract_build_deps(self.REQUIREMENTS, "requirements.txt")
        artifacts = {d.artifact for d in deps}
        assert "flask" in artifacts
        assert "sqlalchemy" in artifacts

    def test_ecosystem(self):
        deps = extract_build_deps(self.PYPROJECT, "pyproject.toml")
        assert all(d.ecosystem == "python" for d in deps)


# ---------------------------------------------------------------------------
# match_deps_to_collections
# ---------------------------------------------------------------------------

class TestMatchDepsToCollections:
    def _dep(self, artifact: str, group: str = "", ecosystem: str = "maven") -> BuildDep:
        return BuildDep(artifact=artifact, group=group, ecosystem=ecosystem)

    def test_exact_match(self):
        deps = [self._dep("my-contracts")]
        matches = match_deps_to_collections(deps, ["my-contracts", "my-engine"])
        assert len(matches) == 1
        assert matches[0]["matched_collection"] == "my-contracts"
        assert matches[0]["match_confidence"] == "exact"

    def test_fuzzy_suffix_strip(self):
        # "my-contracts-definitions" should match collection "my-contracts"
        deps = [self._dep("my-contracts-definitions", group="com.example.contracts")]
        matches = match_deps_to_collections(deps, ["my-contracts", "my-engine"])
        assert any(m["matched_collection"] == "my-contracts" for m in matches)

    def test_group_match(self):
        # group segment containing the collection name should match
        deps = [self._dep("some-artifact", group="com.example.my-contracts")]
        matches = match_deps_to_collections(deps, ["my-contracts"])
        assert len(matches) >= 1

    def test_self_excluded(self):
        deps = [self._dep("my-service")]
        matches = match_deps_to_collections(
            deps, ["my-service", "my-engine"], self_collection="my-service"
        )
        assert not any(m["matched_collection"] == "my-service" for m in matches)

    def test_no_match_short_name(self):
        # Very short names should not cause false positives
        deps = [self._dep("log")]
        matches = match_deps_to_collections(deps, ["logback", "my-logger"])
        # "log" is 3 chars, below the 4-char fuzzy threshold
        assert len(matches) == 0

    def test_case_insensitive(self):
        deps = [self._dep("My-Contracts")]
        matches = match_deps_to_collections(deps, ["my-contracts"])
        assert len(matches) == 1

    def test_no_duplicates(self):
        # Same artifact matched twice should deduplicate by (artifact, collection)
        deps = [
            self._dep("my-contracts-definitions", group="com.example.contracts"),
            self._dep("my-contracts-definitions", group="com.example.contracts"),
        ]
        matches = match_deps_to_collections(deps, ["my-contracts"])
        assert len(matches) == 1


# ---------------------------------------------------------------------------
# Non-manifest files return empty
# ---------------------------------------------------------------------------

class TestNonManifest:
    def test_java_file_returns_empty(self):
        deps = extract_build_deps("public class Foo {}", "src/Foo.java")
        assert deps == []

    def test_unknown_file_returns_empty(self):
        deps = extract_build_deps("<dependency><groupId>x</groupId></dependency>", "some-file.txt")
        assert deps == []
