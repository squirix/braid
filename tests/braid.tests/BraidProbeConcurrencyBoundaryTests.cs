namespace Braid.Tests;

/// <summary>Covers probe concurrency boundaries: overlapping, flowing, and suppressed execution contexts.</summary>
public sealed class BraidProbeConcurrencyBoundaryTests : TestBase
{
    /// <summary>Verifies concurrent probe waits on the same logical worker are rejected instead of being serialized.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ConcurrentProbeHitsSameWorkerFailClearly(CancellationToken cancellationToken)
    {
        return AssertConcurrentProbeRaceMustFailAsync(() => Runner.RunAsync(
            async context =>
            {
                context.Fork(
                    "worker-1",
                    async () =>
                    {
                        var firstProbeInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                        var firstProbeTask = StartNewOnThreadPoolAsync(
                            async () =>
                            {
                                var hitTask = Probe.HitAsync("first", cancellationToken).AsTask();
                                firstProbeInFlight.SetResult();
                                await hitTask;
                            },
                            cancellationToken);

                        await firstProbeInFlight.Task.WaitAsync(cancellationToken);
                        await Probe.HitAsync("second", cancellationToken);
                        await firstProbeTask;
                    });

                context.Fork(
                    "worker-2",
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, cancellationToken);
                        await Probe.HitAsync("other", cancellationToken);
                    });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 12345,
                Timeout = TimeSpan.FromSeconds(2),
                Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "first"), ReplayStep.Hit("worker-2", "other")),
            },
            cancellationToken));
    }

    /// <summary>
    /// Verifies a worker cannot re-enter probe waiting through a flowing child task
    /// before the previous logical probe completes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FlowingChildProbeOverlapsParentFails(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    var childProbeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                    var childTask = StartNewOnThreadPoolAsync(
                        async () =>
                        {
                            var childProbeTask = Probe.HitAsync("child", cancellationToken).AsTask();

                            childProbeEntered.SetResult();

                            await childProbeTask;
                        },
                        cancellationToken);

                    await childProbeEntered.Task.WaitAsync(cancellationToken);

                    await Probe.HitAsync("parent", cancellationToken);

                    await childTask;
                });

                context.Fork(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, cancellationToken);
                    await Probe.HitAsync("other", cancellationToken);
                });

                await context.JoinAsync(cancellationToken);
            },
            new RunOptions
            {
                Iterations = 1,
                Seed = 12345,
                Schedule = ReplaySchedule.Replay(ReplayStep.Arrive("worker-1", "child"), ReplayStep.Hit("worker-2", "other")),
                Timeout = TimeSpan.FromSeconds(2),
            },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        var report = exception.ToString();

        _ = await Assert.That(report).Contains("Concurrent probe hit on the same worker is not supported.");
    }

    /// <summary>Verifies a serialized child task probe after the parent probe completes is allowed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ProbeInsideFlowingAfterParentSucceeds(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("parent", cancellationToken);
                        await StartNewOnThreadPoolAsync(() => Probe.HitAsync("child", cancellationToken).AsTask(), cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Serialized child probe should complete.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies a flowing child task that hits a probe while the parent waits at another probe fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ProbeInsideFlowingFailsOrSerializes(CancellationToken cancellationToken)
    {
        return AssertConcurrentProbeRaceToleratesAsync(() => Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await RunTwoThreadProbeRaceAsync("parent", "child", cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 12345 },
            cancellationToken));
    }

    /// <summary>Verifies probes started under suppressed flow do not bind to the braid worker.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ProbeInsideSuppressedContextCompletes(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "real")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    Task suppressedProbeTask;

                    using (ExecutionContext.SuppressFlow())
                    {
                        suppressedProbeTask = StartNewOnThreadPoolAsync(
                            () => Probe.HitAsync("suppressed", cancellationToken).AsTask(),
                            cancellationToken);
                    }

                    await suppressedProbeTask;

                    await Probe.HitAsync("real", cancellationToken);
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        _ = await Assert.That(options.Schedule.Steps).HasSingleItem();
        await Probe.HitAsync("outside-run", cancellationToken);
    }
}
