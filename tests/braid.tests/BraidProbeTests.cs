using Xunit;

namespace Braid.Tests;

public sealed class BraidProbeTests : TestBase
{
    [Fact]
    public async Task HitAsyncOutsideRunCompletesImmediately() => await BraidProbe.HitAsync("outside-run", DefaultCancellationToken);
}
