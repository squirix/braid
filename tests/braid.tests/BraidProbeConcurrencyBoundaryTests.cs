using Xunit;

namespace Braid.Tests;

/// <summary>Covers probe concurrency boundaries: overlapping, flowing, and suppressed execution contexts.</summary>
public sealed class BraidProbeConcurrencyBoundaryTests : TestBase
{
    /// <summary>Verifies concurrent probe waits on the same logical worker are rejected instead of being serialized.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ConcurrentProbeHitsSameWorkerFailClearly()
    {
        return AssertConcurrentProbeRaceMustFailAsync(static () => Runner.RunAsync(
            static async context =>
            {
                context.Fork(
                    "worker-1",
                    static async () =>
                    {
                        var firstProbeInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                        var firstProbeTask = StartNewOnThreadPoolAsync(
                            async () =>
                            {
                                var hitTask = Probe.HitAsync("first", DefaultCancellationToken).AsTask();
                                firstProbeInFlight.SetResult();
                                await hitTask;
                            },
                            DefaultCancellationToken);

                        await firstProbeInFlight.Task.WaitAsync(DefaultCancellationToken);
                        await Probe.HitAsync("second", DefaultCancellationToken);
                        await firstProbeTask;
                    });

                context.Fork(
                    "worker-2",
                    static async () =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, DefaultCancellationToken);
                        await Probe.HitAsync("other", DefaultCancellationToken);
                    });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 12345,
                Timeout = TimeSpan.FromSeconds(2),
                Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "first"), ReplayStep.Hit("worker-2", "other")),
            },
            DefaultCancellationToken));
    }

    /// <summary>
    /// Verifies a worker cannot re-enter probe waiting through a flowing child task
    /// before the previous logical probe completes.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task FlowingChildProbeOverlapsParentFails()
    {
        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    var childProbeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                    var childTask = StartNewOnThreadPoolAsync(
                        async () =>
                        {
                            var childProbeTask = Probe.HitAsync("child", DefaultCancellationToken).AsTask();

                            childProbeEntered.SetResult();

                            await childProbeTask;
                        },
                        DefaultCancellationToken);

                    await childProbeEntered.Task.WaitAsync(DefaultCancellationToken);

                    await Probe.HitAsync("parent", DefaultCancellationToken);

                    await childTask;
                });

                context.Fork(static async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, DefaultCancellationToken);
                    await Probe.HitAsync("other", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 12345,
                Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "child"), ReplayStep.Hit("worker-2", "other")),
                Timeout = TimeSpan.FromSeconds(2),
            },
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();

        Assert.Contains("Concurrent probe hit on the same worker is not supported.", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies a serialized child task probe after the parent probe completes is allowed.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ProbeInsideFlowingAfterParentSucceeds()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await Probe.HitAsync("parent", DefaultCancellationToken);
                        await StartNewOnThreadPoolAsync(static () => Probe.HitAsync("child", DefaultCancellationToken).AsTask(), DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Serialized child probe should complete.");
    }

    /// <summary>Verifies a flowing child task that hits a probe while the parent waits at another probe fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ProbeInsideFlowingFailsOrSerializes()
    {
        return AssertConcurrentProbeRaceToleratesAsync(static () => Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await RunTwoThreadProbeRaceAsync("parent", "child"));
                await context.JoinAsync(DefaultCancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            DefaultCancellationToken));
    }

    /// <summary>Verifies probes started under suppressed flow do not bind to the braid worker.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeInsideSuppressedContextCompletes()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "real")),
        };

        await Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () =>
                {
                    Task suppressedProbeTask;

                    using (ExecutionContext.SuppressFlow())
                    {
                        suppressedProbeTask = StartNewOnThreadPoolAsync(
                            static () => Probe.HitAsync("suppressed", DefaultCancellationToken).AsTask(),
                            DefaultCancellationToken);
                    }

                    await suppressedProbeTask;

                    await Probe.HitAsync("real", DefaultCancellationToken);
                });

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);

        _ = Assert.Single(options.Schedule.Steps);
        await Probe.HitAsync("outside-run", DefaultCancellationToken);
    }
}
