using TUnit.Core;
using TUnit.Mocks;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>
/// Suite default: Strict mocks (ADR 0034 Phase 1 human decision).
/// </summary>
public static class MockDefaults
{
    [Before(HookType.TestDiscovery)]
    public static void ConfigureStrictMocks(BeforeTestDiscoveryContext context)
    {
        context.Settings.Mocks.DefaultMode = MockBehavior.Strict;
    }
}
