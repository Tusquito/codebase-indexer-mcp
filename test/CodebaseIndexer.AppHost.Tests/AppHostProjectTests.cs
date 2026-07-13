namespace CodebaseIndexer.AppHost.Tests.Tests;

/// <summary>Verifies the AppHost project compiles and loads.</summary>
public sealed class AppHostProjectTests
{
    /// <summary>AppHost project assembly is available at runtime.</summary>
    [Fact]
    public void AppHost_project_builds()
    {
        var appHostAssembly = typeof(Projects.CodebaseIndexer_AppHost).Assembly;
        Assert.NotNull(appHostAssembly);
    }
}
