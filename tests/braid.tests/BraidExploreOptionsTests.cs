using Xunit;

namespace Braid.Tests;

/// <summary>Covers exploration option validation.</summary>
public sealed class BraidExploreOptionsTests : TestBase
{
    /// <summary>Verifies invalid schedule caps are rejected before exploration starts.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreRejectsInvalidMaxSchedulesStart()
    {
        var ran = false;

        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithMaxSchedules(0).Build(),
            async braid =>
            {
                ran = true;
                await braid.WorkerAsync("worker-1", static () => Task.CompletedTask);
            },
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<ArgumentOutOfRangeException>(operation);

        Assert.False(ran);
    }

    /// <summary>Verifies invalid step caps are rejected before exploration starts.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreRejectsInvalidMaxStepsStart()
    {
        var ran = false;

        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithMaxStepsPerSchedule(0).Build(),
            async braid =>
            {
                ran = true;
                await braid.WorkerAsync("worker-1", static () => Task.CompletedTask);
            },
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<ArgumentOutOfRangeException>(operation);

        Assert.False(ran);
    }

    /// <summary>Verifies WithTimeout propagates the configured value to ExploreOptions.</summary>
    [Fact]
    public void WithTimeoutSetsTimeoutOnBuiltOptions()
    {
        var timeout = TimeSpan.FromSeconds(42);
        var options = new ExploreOptionsBuilder().WithTimeout(timeout).Build();

        Assert.Equal(timeout, options.Timeout);
    }

    /// <summary>Verifies zero timeout is rejected before exploration starts.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreRejectsZeroTimeout()
    {
        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithTimeout(TimeSpan.Zero).Build(),
            static async braid => await braid.WorkerAsync("worker-1", static () => Task.CompletedTask),
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<ArgumentOutOfRangeException>(operation);
    }

    /// <summary>Verifies negative timeout is rejected before exploration starts.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreRejectsNegativeTimeout()
    {
        var operation = Runner.ExploreAsync(
            new ExploreOptionsBuilder().WithTimeout(TimeSpan.FromMilliseconds(-1)).Build(),
            static async braid => await braid.WorkerAsync("worker-1", static () => Task.CompletedTask),
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<ArgumentOutOfRangeException>(operation);
    }
}
