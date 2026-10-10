using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers how runs and exploration report timeouts.</summary>
public sealed class BraidExploreTimeoutTests : TestBase
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Verifies a hang in the first run with no parked worker fails exploration with a timeout.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreSurfacesFirstRunTimeout(CancellationToken cancellationToken)
    {
        await using var hang = new HungWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithTimeout(RunTimeout),
                async braid =>
                {
                    await braid.WorkerAsync("w", hang.HangAsync);
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
        _ = await Assert.That(exception.Message).Contains("braid run timed out.");
        _ = await Assert.That(exception.SchedulerDiagnostics!.RunningWorkers).Contains(new ProbeWaitDiagnostic("w", ProbeWaitDiagnostic.StartProbeName));
    }

    /// <summary>Verifies a hang of the last worker under a generated schedule fails exploration with a replay token that reproduces it.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsScheduleTimeout(CancellationToken cancellationToken)
    {
        await using var hang = new HungWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithSeed(1).WithMaxSchedules(10).WithTimeout(RunTimeout),
                async braid =>
                {
                    await braid.WorkerAsync("first", async () => await Probe.HitAsync("a", cancellationToken));
                    await braid.WorkerAsync(
                        "second",
                        async () =>
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            await hang.HangAsync();
                        });
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
        _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
        _ = await Assert.That(exception.Steps).IsEquivalentTo(
            [ReplayStep.Start("first"), ReplayStep.Start("second"), ReplayStep.Hit("first", "a"), ReplayStep.Hit("second", "b")],
            CollectionOrdering.Matching);

        await using var replayHang = new HungWorker();
        var replayed = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("first", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork(
                        "second",
                        async () =>
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            await replayHang.HangAsync();
                        });
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Parse(replayText), Timeout = RunTimeout },
                cancellationToken));

        _ = await Assert.That(replayed.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
    }

    /// <summary>Verifies exploration skips a schedule that hangs only because the released worker waits for a parked one.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreSkipsParkedTimeout(CancellationToken cancellationToken)
    {
        await using var hang = new HungWorker();
        _ = await Assert.That(() => Runner.ExploreAsync(
            static options => options.WithSeed(1).WithMaxSchedules(10).WithTimeout(RunTimeout),
            async braid =>
            {
                var firstDone = false;
                await braid.WorkerAsync(
                    "first",
                    async () =>
                    {
                        await Probe.HitAsync("a", cancellationToken);
                        firstDone = true;
                    });
                await braid.WorkerAsync(
                    "second",
                    async () =>
                    {
                        await Probe.HitAsync("b", cancellationToken);
                        if (!firstDone)
                            await hang.HangAsync();
                    });
                await braid.JoinAsync(cancellationToken);
            },
            cancellationToken)).ThrowsNothing();
    }

    /// <summary>Verifies a timeout while a worker runs and another is parked at a probe is a scheduler failure that names both.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ParkedTimeoutNamesRunningWorker(CancellationToken cancellationToken)
    {
        await using var hang = new HungWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("first", async () => await Probe.HitAsync("a", cancellationToken));
                    context.Fork(
                        "second",
                        async () =>
                        {
                            await Probe.HitAsync("b", cancellationToken);
                            await hang.HangAsync();
                        });
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Parse("hit second b\nhit first a"), Timeout = RunTimeout },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).Contains("parked before their start or at probes");
        _ = await Assert.That(exception.SchedulerDiagnostics!.RunningWorkers).Contains(new ProbeWaitDiagnostic("second", "b"));
        _ = await Assert.That(exception.SchedulerDiagnostics.WaitingWorkers).Contains(new ProbeWaitDiagnostic("first", "a"));
        _ = await Assert.That(exception.ToString()).Contains("Running workers (last released at):");
    }

    /// <summary>Verifies a hang before the first probe while another worker has not started is a scheduler failure that names both.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task UnstartedWorkerTimeoutNamesBoth(CancellationToken cancellationToken)
    {
        await using var hang = new HungWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("first", hang.HangAsync);
                    context.Fork("second", async () => await Probe.HitAsync("b", cancellationToken));
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Timeout = RunTimeout, Schedule = ReplaySchedule.Replay(ReplayStep.Start("first")) },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.SchedulerDiagnostics!.RunningWorkers).Contains(new ProbeWaitDiagnostic("first", ProbeWaitDiagnostic.StartProbeName));
        _ = await Assert.That(exception.SchedulerDiagnostics.WaitingWorkers).Contains(new ProbeWaitDiagnostic("second", ProbeWaitDiagnostic.NotStartedProbeName));
    }

    /// <summary>A worker body that hangs until disposed, then waits for the worker to exit so it cannot outlive the test.</summary>
    private sealed class HungWorker : IAsyncDisposable
    {
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _release = new();
        private int _started;

        public async ValueTask DisposeAsync()
        {
            await _release.CancelAsync();
            if (Volatile.Read(ref _started) != 0)
                await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, CancellationToken.None);

            _release.Dispose();
        }

        public async Task HangAsync()
        {
            _ = Interlocked.Exchange(ref _started, 1);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, _release.Token);
            }
            catch (OperationCanceledException) when (_release.IsCancellationRequested)
            {
                // The test released the hung worker after the run ended.
            }
            finally
            {
                _ = _exited.TrySetResult();
            }
        }
    }
}
