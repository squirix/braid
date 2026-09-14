namespace Braid.Tests;

/// <summary>Covers exploration option validation.</summary>
public sealed class BraidExploreOptionsTests : TestBase
{
    /// <summary>Verifies invalid schedule caps are rejected before exploration starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreRejectsInvalidMaxSchedulesStart(CancellationToken cancellationToken)
    {
        var ran = false;

        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithMaxSchedules(0).Build(),
            async braid =>
            {
                ran = true;
                await braid.WorkerAsync("worker-1", static () => Task.CompletedTask);
            },
            cancellationToken);

        _ = await BraidAssertions.AssertExpectsAsync<ArgumentOutOfRangeException>(operation);

        _ = await Assert.That(ran).IsFalse();
    }

    /// <summary>Verifies invalid step caps are rejected before exploration starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreRejectsInvalidMaxStepsStart(CancellationToken cancellationToken)
    {
        var ran = false;

        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithMaxStepsPerSchedule(0).Build(),
            async braid =>
            {
                ran = true;
                await braid.WorkerAsync("worker-1", static () => Task.CompletedTask);
            },
            cancellationToken);

        _ = await BraidAssertions.AssertExpectsAsync<ArgumentOutOfRangeException>(operation);

        _ = await Assert.That(ran).IsFalse();
    }

    /// <summary>Verifies WithTimeout propagates the configured value to ExploreOptions.</summary>
    [Test]
    public async Task WithTimeoutSetsTimeoutOnBuiltOptions()
    {
        var timeout = TimeSpan.FromSeconds(42);
        var options = new ExploreOptionsBuilder().WithTimeout(timeout).Build();

        _ = await Assert.That(options.Timeout).IsEqualTo(timeout);
    }

    /// <summary>Verifies zero timeout is rejected before exploration starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreRejectsZeroTimeout(CancellationToken cancellationToken)
    {
        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithTimeout(TimeSpan.Zero).Build(),
            static async braid => await braid.WorkerAsync("worker-1", static () => Task.CompletedTask),
            cancellationToken);

        _ = await BraidAssertions.AssertExpectsAsync<ArgumentOutOfRangeException>(operation);
    }

    /// <summary>Verifies negative timeout is rejected before exploration starts.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreRejectsNegativeTimeout(CancellationToken cancellationToken)
    {
        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithTimeout(TimeSpan.FromMilliseconds(-1)).Build(),
            static async braid => await braid.WorkerAsync("worker-1", static () => Task.CompletedTask),
            cancellationToken);

        _ = await BraidAssertions.AssertExpectsAsync<ArgumentOutOfRangeException>(operation);
    }
}
