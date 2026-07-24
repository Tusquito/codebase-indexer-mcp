using Microsoft.Testing.Platform.Builder;

namespace CodebaseIndexer.Tests.Shared;

/// <summary>
/// MTP builder hook that registers <see cref="NoLogoCommandLineOptionsProvider"/>.
/// </summary>
public static class NoLogoCompatBuilderHook
{
    /// <summary>
    /// Registers the <c>--nologo</c> compatibility command-line option provider.
    /// </summary>
    /// <param name="testApplicationBuilder">The MTP test application builder.</param>
    /// <param name="arguments">Unused; required by <c>TestingPlatformBuilderHook</c> contract.</param>
    public static void AddExtensions(ITestApplicationBuilder testApplicationBuilder, string[] arguments)
    {
        _ = arguments;
        testApplicationBuilder.CommandLine.AddProvider(static () => new NoLogoCommandLineOptionsProvider());
    }
}
