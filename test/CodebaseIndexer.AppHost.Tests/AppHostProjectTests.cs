namespace CodebaseIndexer.AppHost.Tests.Tests;

public sealed class AppHostProjectTests
{
    [Fact]
    public void AppHost_project_builds()
    {
        var appHostAssembly = typeof(Projects.CodebaseIndexer_AppHost).Assembly;
        Assert.NotNull(appHostAssembly);
    }
}
