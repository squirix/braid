using Xunit;

namespace Braid.Tests;

/// <summary>Covers bounded schedule exploration.</summary>
public sealed class BraidExploreAsyncTests : TestBase
{
    /// <summary>Verifies exploration finds a lost update and exports a replay token.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreAsyncFindsLostUpdateReplayToken()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(12_345).WithMaxSchedules(100).WithMaxStepsPerSchedule(10),
                RunLostUpdateExploreAsync,
                DefaultCancellationToken));

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Contains("after-read", replayText, StringComparison.Ordinal);
        Assert.Contains("before-write", replayText, StringComparison.Ordinal);

        var schedule = ReplaySchedule.Parse(replayText);
        _ = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    var value = 0;

                    context.Fork(
                        "worker-1",
                        async () =>
                        {
                            var current = value;
                            await Probe.HitAsync("after-read", DefaultCancellationToken);
                            await Probe.HitAsync("before-write", DefaultCancellationToken);
                            value = current + 1;
                        });

                    context.Fork(
                        "worker-2",
                        async () =>
                        {
                            var current = value;
                            await Probe.HitAsync("after-read", DefaultCancellationToken);
                            await Probe.HitAsync("before-write", DefaultCancellationToken);
                            value = current + 1;
                        });

                    await context.JoinAsync(DefaultCancellationToken);
                    Assert.Equal(2, value);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 12_345,
                    Schedule = schedule,
                },
                DefaultCancellationToken));
    }

    /// <summary>Verifies the same seed and bounds report the same first failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreAsyncIsDeterministicForSeedBounds()
    {
        var first = await ExploreLostUpdateAsync();
        var second = await ExploreLostUpdateAsync();

        Assert.True(first.TryGetReplayText(out var firstToken, out var firstError), firstError);
        Assert.True(second.TryGetReplayText(out var secondToken, out var secondError), secondError);
        Assert.Equal(firstToken, secondToken);
    }

    /// <summary>Verifies exploration registers workers with stable ids.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreAsyncRegistersStableWorkerIds()
    {
        var first = await ExploreReaderWriterStableIdsAsync();
        var second = await ExploreReaderWriterStableIdsAsync();

        AssertStableReaderWriterIds(first);
        AssertStableReaderWriterIds(second);

        Assert.True(first.TryGetReplayText(out var firstReplay, out var firstError), firstError);
        Assert.True(second.TryGetReplayText(out var secondReplay, out var secondError), secondError);
        Assert.Equal(firstReplay, secondReplay);
    }

    /// <summary>Verifies named worker ids are used in generated replay schedules.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreAsyncUsesNamedWorkerIdsReplay()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(12_345).WithMaxSchedules(100).WithMaxStepsPerSchedule(10),
                RunNamedLostUpdateExploreAsync,
                DefaultCancellationToken));

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Contains("reader", replayText, StringComparison.Ordinal);
        Assert.Contains("writer", replayText, StringComparison.Ordinal);
    }

    /// <summary>Verifies exploration completes when no assertion failure exists within bounds.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreCompletesNoFailureWithinBounds()
    {
        var explored = false;

        await Runner.ExploreAsync(
            static options => options.WithSeed(99).WithMaxSchedules(5).WithMaxStepsPerSchedule(4),
            async braid =>
            {
                await braid.WorkerAsync("worker-1", static async () => await Probe.HitAsync("only-probe", DefaultCancellationToken));
                await braid.JoinAsync(DefaultCancellationToken);
                explored = true;
            },
            DefaultCancellationToken);

        Assert.True(explored);
    }

    /// <summary>Verifies a step cap below the required schedule length prevents finding a failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreCompletesSmallStepsForLostUpdate()
    {
        var exception = await Record.ExceptionAsync(static () => Runner.ExploreAsync(
            static options => options.WithSeed(77).WithMaxSchedules(100).WithMaxStepsPerSchedule(3),
            RunLostUpdateExploreAsync,
            DefaultCancellationToken));

        Assert.Null(exception);
    }

    /// <summary>Verifies exhausting MaxSchedules returns without failure when only a passing schedule is evaluated.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreCompletesWhenOnlyPassingSchedule()
    {
        var exception = await Record.ExceptionAsync(static () => Runner.ExploreAsync(
            static options => options.WithSeed(99).WithMaxSchedules(1).WithMaxStepsPerSchedule(2),
            static async braid =>
            {
                await braid.WorkerAsync("worker-1", static async () => await Probe.HitAsync("only-probe", DefaultCancellationToken));
                await braid.JoinAsync(DefaultCancellationToken);
            },
            DefaultCancellationToken));

        Assert.Null(exception);
    }

    /// <summary>Verifies user InvalidOperationException failures are not suppressed during discovery.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ExploreSurfacesUserInvalidOpDiscovery()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(7).WithMaxSchedules(10).WithMaxStepsPerSchedule(4),
                static _ => throw new InvalidOperationException("user test failure"),
                DefaultCancellationToken));

        Assert.Equal(RunFailureOrigin.UserTest, exception.FailureOrigin);
        _ = Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static void AssertStableReaderWriterIds(RunException exception)
    {
        Assert.Contains("reader forked", exception.Traces, StringComparer.Ordinal);
        Assert.Contains("writer forked", exception.Traces, StringComparer.Ordinal);
        Assert.Contains("reader hit ready", exception.Traces, StringComparer.Ordinal);
        Assert.Contains("writer hit ready", exception.Traces, StringComparer.Ordinal);
        var hasWorkerPrefix = false;
        foreach (var line in exception.Traces)
        {
            if (!line.StartsWith("worker-", StringComparison.Ordinal))
                continue;
            hasWorkerPrefix = true;
            break;
        }

        Assert.False(hasWorkerPrefix, "Trace should not contain worker-prefixed entries.");

        Assert.True(exception.TryGetReplayText(out var replayText, out var error), error);
        Assert.Contains("reader", replayText, StringComparison.Ordinal);
        Assert.Contains("writer", replayText, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-", replayText, StringComparison.Ordinal);

        Assert.Contains(
            exception.Steps,
            static step => string.Equals(step.WorkerId, "reader", StringComparison.Ordinal) && string.Equals(step.ProbeName, "ready", StringComparison.Ordinal));
        Assert.Contains(
            exception.Steps,
            static step => string.Equals(step.WorkerId, "writer", StringComparison.Ordinal) && string.Equals(step.ProbeName, "ready", StringComparison.Ordinal));
    }

    private static Task<RunException> ExploreLostUpdateAsync() => Assertions.ExpectsAsync<RunException>(
        Runner.ExploreAsync(static options => options.WithSeed(77).WithMaxSchedules(40).WithMaxStepsPerSchedule(10), RunLostUpdateExploreAsync, DefaultCancellationToken));

    private static Task<RunException> ExploreReaderWriterStableIdsAsync() => Assertions.ExpectsAsync<RunException>(
        Runner.ExploreAsync(
            static options => options.WithSeed(5).WithMaxSchedules(10).WithMaxStepsPerSchedule(4),
            RegisterReaderWriterWorkersAsync,
            DefaultCancellationToken));

    private static async Task RegisterReaderWriterWorkersAsync(ExploreContext braid)
    {
        var completedWorkers = 0;

        await braid.WorkerAsync(
            "reader",
            async () =>
            {
                await Probe.HitAsync("ready", DefaultCancellationToken);
                _ = Interlocked.Increment(ref completedWorkers);
            });

        await braid.WorkerAsync(
            "writer",
            async () =>
            {
                await Probe.HitAsync("ready", DefaultCancellationToken);
                _ = Interlocked.Increment(ref completedWorkers);
            });

        await braid.JoinAsync(DefaultCancellationToken);
        Assert.Equal(0, completedWorkers);
    }

    private static async Task RunLostUpdateExploreAsync(ExploreContext braid)
    {
        var value = 0;

        await braid.WorkerAsync(
            "worker-1",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", DefaultCancellationToken);
                await Probe.HitAsync("before-write", DefaultCancellationToken);
                value = current + 1;
            });

        await braid.WorkerAsync(
            "worker-2",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", DefaultCancellationToken);
                await Probe.HitAsync("before-write", DefaultCancellationToken);
                value = current + 1;
            });

        await braid.JoinAsync(DefaultCancellationToken);
        Assert.Equal(2, value);
    }

    private static async Task RunNamedLostUpdateExploreAsync(ExploreContext braid)
    {
        var value = 0;

        await braid.WorkerAsync(
            "reader",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", DefaultCancellationToken);
                await Probe.HitAsync("before-write", DefaultCancellationToken);
                value = current + 1;
            });

        await braid.WorkerAsync(
            "writer",
            async () =>
            {
                var current = value;
                await Probe.HitAsync("after-read", DefaultCancellationToken);
                await Probe.HitAsync("before-write", DefaultCancellationToken);
                value = current + 1;
            });

        await braid.JoinAsync(DefaultCancellationToken);
        Assert.Equal(2, value);
    }
}
