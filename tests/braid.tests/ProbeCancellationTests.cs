namespace Braid.Tests;

/// <summary>Covers a probe whose token is canceled while the worker is parked at it, or before the worker hits it.</summary>
public sealed class ProbeCancellationTests : TestBase
{
    /// <summary>Verifies a worker whose probe token another worker cancels stays parked until the scheduler releases it, so the two never run at once.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CanceledProbeWaitsForItsRelease(CancellationToken cancellationToken)
    {
        using var probeCts = new CancellationTokenSource();
        var scenario = new CancelWhileParked(probeCts, cancellationToken);
        var schedule = ReplaySchedule.Replay(ReplayStep.Hit("b", "b1"), ReplayStep.Hit("a", "a1"), ReplayStep.Hit("b", "b2"), ReplayStep.Hit("a", "a2"));

        var trace = await scenario.RunAsync(new RunOptions { Iterations = 1, Seed = 1, Schedule = schedule });

        _ = await Assert.That(scenario.Overlaps).IsEqualTo(0);
        _ = await Assert.That(scenario.CanceledWith).IsEqualTo(probeCts.Token);
        _ = await Assert.That(trace).IsEqualTo(
            "a forked | b forked | a released | a hit a1 | b released | b hit b1 | b released at b1 | b hit b2 | a released at a1 | a hit a2 | b released at b2 | b completed | a released at a2 | a completed");
    }

    /// <summary>Verifies a random run in which a worker cancels another worker's probe token gives the same trace every time for one seed.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task SameSeedGivesSameTraceWhenCanceled(int seed, CancellationToken cancellationToken)
    {
        var traces = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var probeCts = new CancellationTokenSource();
            var scenario = new CancelWhileParked(probeCts, cancellationToken);
            _ = traces.Add(await scenario.RunAsync(new RunOptions { Iterations = 1, Seed = seed }));
            _ = await Assert.That(scenario.Overlaps).IsEqualTo(0);
        }

        _ = await Assert.That(traces).HasSingleItem();
    }

    /// <summary>Verifies a probe hit with an already canceled token is still a scheduling point and throws when the worker is released.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CanceledTokenProbeIsASchedulingPoint(CancellationToken cancellationToken)
    {
        using var probeCts = new CancellationTokenSource();
        await probeCts.CancelAsync();
        var probeToken = probeCts.Token;
        var canceled = 0;
        var releases = 0;

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(
                    "retrying",
                    async () =>
                    {
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            try
                            {
                                await Probe.HitAsync("retry", probeToken);
                            }
                            catch (OperationCanceledException)
                            {
                                canceled++;
                            }
                        }
                    });
                context.Fork("other", async () => await Probe.HitAsync("o", cancellationToken));
                await context.JoinAsync(cancellationToken);
                foreach (var step in context.TraceSteps)
                {
                    if (string.Equals(step, "retrying released at retry", StringComparison.Ordinal))
                        releases++;
                }
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(canceled).IsEqualTo(3);
        _ = await Assert.That(releases).IsEqualTo(3);
    }

    /// <summary>
    /// Verifies a worker that cancels another worker's probe token and then waits for that worker before its own next probe times out as blocked on a parked worker:
    /// the canceled worker observes the cancellation only when the scheduler releases it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WaitingForCanceledParkedWorkerTimesOut(CancellationToken cancellationToken)
    {
        using var probeCts = new CancellationTokenSource();
        var probeToken = probeCts.Token;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(
                        "consumer",
                        async () =>
                        {
                            try
                            {
                                await Probe.HitAsync("wait", probeToken);
                            }
                            finally
                            {
                                stopped.SetResult();
                            }
                        });
                    context.Fork(
                        "stopper",
                        async () =>
                        {
                            await Probe.HitAsync("before-stop", cancellationToken);
                            await probeCts.CancelAsync();
                            await stopped.Task.WaitAsync(cancellationToken);
                        });
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 1,
                    Timeout = TimeSpan.FromMilliseconds(300),
                    Schedule = ReplaySchedule.Replay(ReplayStep.Hit("stopper", "before-stop"), ReplayStep.Hit("consumer", "wait")),
                },
                cancellationToken));

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Scheduler);
        _ = await Assert.That(exception.Message).Contains("it may be waiting for one of them");
    }

    /// <summary>Worker a parks at a probe with its own token; worker b cancels that token while it runs. Both do some work after that.</summary>
    private sealed class CancelWhileParked
    {
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource _probeCts;
        private int _overlaps;
        private int _running;

        internal CancelWhileParked(CancellationTokenSource probeCts, CancellationToken cancellationToken)
        {
            _probeCts = probeCts;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Gets the token of the cancellation that worker a observed at its probe.</summary>
        internal CancellationToken CanceledWith { get; private set; }

        /// <summary>Gets the number of times a worker started its work while the other one was working.</summary>
        internal int Overlaps => Volatile.Read(ref _overlaps);

        /// <summary>Runs the scenario once.</summary>
        /// <param name="options">The run options.</param>
        /// <returns>The trace of the run.</returns>
        internal async Task<string> RunAsync(RunOptions options)
        {
            var trace = string.Empty;
            await Runner.RunAsync(
                async context =>
                {
                    context.Fork("a", RunWorkerAAsync);
                    context.Fork("b", RunWorkerBAsync);
                    await context.JoinAsync(_cancellationToken);
                    trace = string.Join(" | ", context.TraceSteps);
                },
                options,
                _cancellationToken);
            return trace;
        }

        private async Task RunWorkerAAsync()
        {
            try
            {
                await Probe.HitAsync("a1", _probeCts.Token);
            }
            catch (OperationCanceledException ex)
            {
                CanceledWith = ex.CancellationToken;
            }

            await WorkAsync();
            await Probe.HitAsync("a2", _cancellationToken);
        }

        private async Task RunWorkerBAsync()
        {
            await Probe.HitAsync("b1", _cancellationToken);
            await _probeCts.CancelAsync();
            await WorkAsync();
            await Probe.HitAsync("b2", _cancellationToken);
        }

        private async Task WorkAsync()
        {
            if (Interlocked.Increment(ref _running) > 1)
                _ = Interlocked.Increment(ref _overlaps);

            await Task.Delay(TimeSpan.FromMilliseconds(30), TimeProvider.System, _cancellationToken);
            _ = Interlocked.Decrement(ref _running);
        }
    }
}
