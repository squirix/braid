using System.Diagnostics;

namespace Braid.Tests;

/// <summary>Covers workers that are still running after a failed run stops waiting for them.</summary>
public sealed class AbandonedWorkerTests : TestBase
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Verifies the failure names the abandoned worker only, and its probe after the run is canceled instead of hitting a disposed scheduler.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task AbandonedWorkerIsReportedAndCanceled(CancellationToken cancellationToken)
    {
        using var stuck = new StuckWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("parked", async () => await Probe.HitAsync("p", cancellationToken));
                    context.Fork("stuck", stuck.RunAsync);
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Timeout = RunTimeout },
                cancellationToken));

        var lateProbe = await stuck.ResumeAsync(cancellationToken);

        _ = await Assert.That(exception.Message).Contains("Workers still running after the run stopped were abandoned: stuck.");
        _ = await Assert.That(exception.ToString()).Contains("were abandoned: ");
        _ = await Assert.That(exception.StackTrace).Contains("JoinAsync");
        _ = await Assert.That(lateProbe).IsEqualTo(TaskStatus.Canceled);
    }

    /// <summary>Verifies a callback failure raised after a failed join names the abandoned worker once.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureReportsAbandonedWorker(CancellationToken cancellationToken)
    {
        using var stuck = new StuckWorker();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork("stuck", stuck.RunAsync);
                    var join = await BraidAssertions.AssertExpectsAsync<RunException>(context.JoinAsync(cancellationToken));
                    throw new InvalidOperationException("callback failed after: " + join.Message);
                },
                new RunOptions { Iterations = 1, Seed = 1, Timeout = RunTimeout },
                cancellationToken));

        _ = await stuck.ResumeAsync(cancellationToken);

        _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
        _ = await Assert.That(exception.InnerException!.Message).DoesNotContain("abandoned");
        _ = await Assert.That(exception.Message.Split("were abandoned").Length).IsEqualTo(2);
        _ = await Assert.That(exception.Message).Contains("abandoned: stuck.");
    }

    /// <summary>Verifies a run that fails while a worker ignores the shutdown waits for that worker once, about one second, not twice.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task FailedRunWaitsOnceForStuckWorker(CancellationToken cancellationToken)
    {
        using var stuck = new StuckWorker();
        var stopwatch = Stopwatch.StartNew();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork("stuck", stuck.RunAfterCanceledProbeAsync);
                    context.Fork(
                        "failing",
                        async () =>
                        {
                            await Probe.HitAsync("q", cancellationToken);
                            throw new InvalidOperationException("worker failed");
                        });
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Replay(new ReplayStep("failing", "q")) },
                cancellationToken));
        var elapsed = stopwatch.Elapsed;

        _ = await stuck.ResumeAsync(cancellationToken);

        // The failed join waits one second for the stuck worker; a second wait when the run stops would take the run past two seconds.
        _ = await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(1.8));
        _ = await Assert.That(exception.InnerException!.Message).IsEqualTo("worker failed");
        _ = await Assert.That(exception.Message).Contains("abandoned: stuck.");
    }

    /// <summary>Verifies a failure whose workers all stop during the shutdown drain does not mention abandoned workers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task DrainedWorkersAreNotReported(CancellationToken cancellationToken)
    {
        // The running worker stops when the shutdown cancels the parked worker's probe.
        using var parkedStopped = new CancellationTokenSource();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork(
                        "parked",
                        async () =>
                        {
                            try
                            {
                                await Probe.HitAsync("p", cancellationToken);
                            }
                            finally
                            {
                                await parkedStopped.CancelAsync();
                            }
                        });
                    context.Fork(
                        "running",
                        async () => await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, parkedStopped.Token)
                            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing));
                    return context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Timeout = RunTimeout },
                cancellationToken));

        _ = await Assert.That(exception.Message).DoesNotContain("abandoned");
    }

    /// <summary>A worker that ignores cancellation until the test resumes it after the run, then hits one more probe.</summary>
    private sealed class StuckWorker : IDisposable
    {
        private readonly TaskCompletionSource<TaskStatus> _lateProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _resume = new();

        public void Dispose() => _resume.Dispose();

        public async Task<TaskStatus> ResumeAsync(CancellationToken cancellationToken)
        {
            await _resume.CancelAsync();
            return await _lateProbe.Task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, cancellationToken);
        }

        /// <summary>Parks at a probe, and when the shutdown of the run cancels that probe, ignores it like <see cref="RunAsync" />.</summary>
        /// <returns>A task that completes after the test resumes the worker.</returns>
        public async Task RunAfterCanceledProbeAsync()
        {
            await Probe.HitAsync("p", CancellationToken.None).AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await RunAsync();
        }

        public async Task RunAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, _resume.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            var probe = Probe.HitAsync("late", CancellationToken.None).AsTask();
            await probe.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            _ = _lateProbe.TrySetResult(probe.Status);
        }
    }
}
