using Xunit;

namespace Braid.Tests;

public abstract class TestBase
{
    protected static CancellationToken DefaultCancellationToken => TestContext.Current.CancellationToken;
}
