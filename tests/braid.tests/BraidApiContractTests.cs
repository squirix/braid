using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers the public braid API contract.
/// </summary>
public sealed class BraidApiContractTests : TestBase
{
    /// <summary>
    /// Verifies run validation rejects a null test delegate.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncThrowsForNullTestDelegate()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(static async () =>
        {
            await Braid.RunAsync(null!, cancellationToken: DefaultCancellationToken);
        });
    }

    /// <summary>
    /// Verifies null options use default options.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAcceptsNullOptions()
    {
        var ran = false;

        await Braid.RunAsync(
            context =>
            {
                _ = context;
                ran = true;
                return Task.CompletedTask;
            },
            null,
            DefaultCancellationToken);

        Assert.True(ran);
    }

    /// <summary>
    /// Verifies invalid iteration counts are rejected before the run starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncRejectsInvalidIterationsBeforeRunStarts()
    {
        var ran = false;

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    ran = true;
                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = 0 },
                DefaultCancellationToken);
        });

        Assert.False(ran);
    }

    /// <summary>
    /// Verifies invalid timeouts are rejected before the run starts.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncRejectsInvalidTimeoutBeforeRunStarts()
    {
        var ran = false;

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Braid.RunAsync(
                context =>
                {
                    _ = context;
                    ran = true;
                    return Task.CompletedTask;
                },
                new BraidOptions { Timeout = TimeSpan.Zero },
                DefaultCancellationToken);
        });

        Assert.False(ran);
    }

    /// <summary>
    /// Verifies fork validation rejects a null operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkThrowsForNullOperation()
    {
        await Braid.RunAsync(
            context =>
            {
                _ = Assert.Throws<ArgumentNullException>(() => context.Fork(null!));
                return Task.CompletedTask;
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies fork after join starts fails with a braid run exception.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ForkAfterJoinStartedFailsClearly()
    {
        await Braid.RunAsync(
            static async context =>
            {
                await context.JoinAsync(DefaultCancellationToken);

                var exception = Assert.Throws<BraidRunException>(() => context.Fork(static () => Task.CompletedTask));
                Assert.Contains("Cannot fork after JoinAsync has started.", exception.Message, StringComparison.Ordinal);
            },
            new BraidOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken);
    }

    /// <summary>
    /// Verifies probe validation rejects invalid names.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HitAsyncRejectsInvalidProbeNames()
    {
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(null!, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(string.Empty, DefaultCancellationToken));
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(static async () => await BraidProbe.HitAsync(" ", DefaultCancellationToken));
    }

    /// <summary>
    /// Verifies replay schedules snapshot the supplied steps.
    /// </summary>
    [Fact]
    public void ReplaySnapshotsInputArray()
    {
        var steps = new[] { new BraidStep("worker-1", "ready") };

        var schedule = BraidSchedule.Replay(steps);
        steps[0] = new BraidStep("worker-2", "changed");

        Assert.Equal(new BraidStep("worker-1", "ready"), schedule.Steps[0]);
    }

    /// <summary>
    /// Verifies replay validation rejects a null steps array.
    /// </summary>
    [Fact]
    public void ReplayThrowsForNullStepsArray() => _ = Assert.Throws<ArgumentNullException>(static () => BraidSchedule.Replay(null!));

    /// <summary>
    /// Verifies braid run exceptions snapshot trace and schedule values.
    /// </summary>
    [Fact]
    public void BraidRunExceptionSnapshotsTraceAndSchedule()
    {
        var trace = new[] { "worker-1 forked" };
        var schedule = new[] { new BraidStep("worker-1", "ready") };

        var exception = new BraidRunException("failed", 12345, 0, trace, schedule, null);
        trace[0] = "changed";
        schedule[0] = new BraidStep("worker-2", "changed");

        Assert.Equal(["worker-1 forked"], exception.Trace);
        Assert.Equal([new BraidStep("worker-1", "ready")], exception.Schedule);
    }

    /// <summary>
    /// Verifies a null schedule is exposed as an empty schedule.
    /// </summary>
    [Fact]
    public void BraidRunExceptionExposesNullScheduleAsEmpty()
    {
        var exception = new BraidRunException("failed", 12345, 0, ["trace"], null, null);

        Assert.Empty(exception.Schedule);
    }
}
