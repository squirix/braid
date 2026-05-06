using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers explicit probe behavior.
/// </summary>
public sealed class BraidProbeTests : TestBase
{
    /// <summary>
    /// Verifies probes are no-ops outside a braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncOutsideRunCompletesImmediately() => await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
}
