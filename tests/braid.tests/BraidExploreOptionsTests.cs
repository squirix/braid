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

        var operation = BraidRunner.ExploreAsync(
            new BraidExploreOptionsBuilder().WithMaxSchedules(0).Build(),
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

        var operation = BraidRunner.ExploreAsync(
            new BraidExploreOptionsBuilder().WithMaxStepsPerSchedule(0).Build(),
            async braid =>
            {
                ran = true;
                await braid.WorkerAsync("worker-1", static () => Task.CompletedTask);
            },
            DefaultCancellationToken);

        _ = await Assertions.ExpectsAsync<ArgumentOutOfRangeException>(operation);

        Assert.False(ran);
    }
}
