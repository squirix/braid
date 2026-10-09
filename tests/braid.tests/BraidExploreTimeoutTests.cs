namespace Braid.Tests;

/// <summary>Covers how runs and exploration report timeouts.</summary>
public sealed class BraidExploreTimeoutTests : TestBase
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>Verifies a hang in the discovery run with no parked worker fails exploration with a timeout.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreSurfacesDiscoveryTimeout(CancellationToken cancellationToken)
    {
        using var hang = new CancellationTokenSource();
        var workerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.ExploreAsync(
                    static options => options.WithSeed(1).WithTimeout(RunTimeout),
                    async braid =>
                    {
                        await braid.WorkerAsync("w", () => HangAsync(workerExited, hang.Token));
                        await braid.JoinAsync(cancellationToken);
                    },
                    cancellationToken));

            _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
            _ = await Assert.That(exception.Message).Contains("braid run timed out.");
        }
        finally
        {
            await hang.CancelAsync();
            await workerExited.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken);
        }
    }

    /// <summary>Verifies a timeout while workers are parked is reported as a scheduler failure that names the running worker.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ParkedTimeoutNamesRunningWorker(CancellationToken cancellationToken)
    {
        using var hang = new CancellationTokenSource();
        var workerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
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
                                await HangAsync(workerExited, hang.Token);
                            });
                        return context.JoinAsync(cancellationToken);
                    },
                    new RunOptions
                    {
                        Iterations = 1,
                        Seed = 1,
                        Schedule = ReplaySchedule.Parse("hit second b\nhit first a"),
                        Timeout = RunTimeout,
                    },
                    cancellationToken));

            _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
            _ = await Assert.That(exception.Message).Contains("parked at probes");
            _ = await Assert.That(exception.SchedulerDiagnostics!.RunningWorkers).Contains(new ProbeWaitDiagnostic("second", "b"));
            _ = await Assert.That(exception.SchedulerDiagnostics.WaitingWorkers).Contains(new ProbeWaitDiagnostic("first", "a"));
            _ = await Assert.That(exception.ToString()).Contains("Running workers (last released at):");
        }
        finally
        {
            await hang.CancelAsync();
            await workerExited.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, cancellationToken);
        }
    }

    private static async Task HangAsync(TaskCompletionSource exited, CancellationToken hang)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, hang);
        }
        catch (OperationCanceledException) when (hang.IsCancellationRequested)
        {
            // The test released the hung worker after the run ended.
        }
        finally
        {
            _ = exited.TrySetResult();
        }
    }
}
