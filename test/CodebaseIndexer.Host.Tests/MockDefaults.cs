using TUnit.Core;
using TUnit.Mocks;

namespace CodebaseIndexer.Host.Tests;

/// <summary>
/// Suite default: Strict mocks (ADR 0034 Phase 2).
/// </summary>
public static class MockDefaults
{
    [Before(HookType.TestDiscovery)]
    public static void ConfigureStrictMocks(BeforeTestDiscoveryContext context)
    {
        context.Settings.Mocks.DefaultMode = MockBehavior.Strict;
    }
}
