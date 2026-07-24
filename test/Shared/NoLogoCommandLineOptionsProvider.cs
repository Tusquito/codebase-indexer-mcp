using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace CodebaseIndexer.Tests.Shared;

/// <summary>
/// Accepts VSTest-era <c>--nologo</c> forwarded by MTP-mode <c>dotnet test</c>,
/// so legacy scripts/harnesses that pass that flag do not fail with exit code 5.
/// </summary>
internal sealed class NoLogoCommandLineOptionsProvider : ICommandLineOptionsProvider
{
    private static readonly CommandLineOption NoLogoOption = new(
        "nologo",
        "Compatibility no-op for VSTest-era --nologo when forwarded by dotnet test MTP mode.",
        ArgumentArity.Zero,
        isHidden: true);

    public string Uid { get; } = "codebase-indexer.nologo-compat";

    public string Version { get; } = "1.0.0";

    public string DisplayName { get; } = "VSTest --nologo compatibility";

    public string Description { get; } =
        "Registers --nologo as a no-op so MTP-mode dotnet test does not reject it.";

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() => [NoLogoOption];

    public Task<ValidationResult> ValidateOptionArgumentsAsync(
        CommandLineOption commandOption,
        string[] arguments) =>
        ValidationResult.ValidTask;

    public Task<ValidationResult> ValidateCommandLineOptionsAsync(
        ICommandLineOptions commandLineOptions) =>
        ValidationResult.ValidTask;
}
