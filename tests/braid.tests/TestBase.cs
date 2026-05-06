using Xunit;

namespace Braid.Tests;

/// <summary>
/// Provides shared test helpers.
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// Gets the xUnit cancellation token for the current test.
    /// </summary>
    protected static CancellationToken DefaultCancellationToken => TestContext.Current.CancellationToken;
}
