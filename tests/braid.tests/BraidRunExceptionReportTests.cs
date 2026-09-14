using System.Globalization;

namespace Braid.Tests;

/// <summary>Covers run exception report behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidRunExceptionReportTests : TestBase
{
    /// <summary>Verifies exception schedule snapshots are immutable for callers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task BraidRunExceptionScheduleCannotBeMutated(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("actual", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 33,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var list = await Assert.That(exception.Steps).IsTypeOf<IList<ReplayStep>>();
        _ = BraidAssertions.AssertExpectsAny<Exception, IList<ReplayStep>>(list!, static state => state[0] = new ReplayStep("worker-9", "changed"));

        _ = await Assert.That(exception.ToString()).Contains("worker-1 @ expected");
    }

    /// <summary>Verifies exception trace snapshots are immutable for callers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task BraidRunExceptionTraceCannotBeMutated(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("actual", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 34,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var traceList = await Assert.That(exception.Traces).IsTypeOf<IList<string>>();
        _ = BraidAssertions.AssertExpectsAny<Exception, IList<string>>(traceList!, static state => state[0] = "mutated");

        _ = await Assert.That(exception.ToString()).Contains("worker-1 hit actual");
    }

    /// <summary>Verifies callback failures after forking include fork trace entries.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureAfterForkReportsTrace(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    throw new InvalidOperationException("fail-after-fork");
                },
                new RunOptions { Iterations = 1, Seed = 27 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("fail-after-fork");
        _ = await Assert.That(report).Contains("worker-1 forked");
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies callback failures before forking still report a trace section.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureBeforeForkEmptyTrace(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(static _ => throw new InvalidOperationException("fail-before-fork"), new RunOptions { Iterations = 1, Seed = 26 }, cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("fail-before-fork");
        _ = await Assert.That(report).Contains("Trace:");
        _ = await Assert.That(report).DoesNotContain("worker-");
    }

    /// <summary>Verifies a worker failure before any probe is surfaced on join with trace context.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsWorkerFailureFirstProbe(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("before-probe failure")));

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("before-probe failure");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies exception properties are reflected in formatted reports.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunExceptionPropertiesMatchReport(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("actual", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 32,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains(exception.Seed.ToString(CultureInfo.InvariantCulture));
        _ = await Assert.That(report).Contains(exception.Iteration.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < exception.Steps.Count; index++)
        {
            var step = exception.Steps[index];
            _ = await Assert.That(report).Contains($"{step.WorkerId} @ {step.ProbeName}");
        }

        for (var index = 0; index < exception.Traces.Count; index++)
        {
            var traceEntry = exception.Traces[index];
            _ = await Assert.That(report).Contains(traceEntry);
        }
    }

    /// <summary>Verifies deterministic seeds produce stable failure reports.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task SameSeedLargeScenarioSameReport(CancellationToken cancellationToken)
    {
        var first = await RunOnceAsync(cancellationToken);
        var second = await RunOnceAsync(cancellationToken);
        _ = await Assert.That(second).IsEqualTo(first);
        return;

        static async Task<string> RunOnceAsync(CancellationToken cancellationToken)
        {
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        for (var workerIndex = 0; workerIndex < 4; workerIndex++)
                            ForkWorkerDeterministicProbes(context, workerIndex, cancellationToken: cancellationToken);

                        await context.JoinAsync(cancellationToken);
                        throw new InvalidOperationException("forced deterministic callback failure");
                    },
                    new RunOptions { Iterations = 1, Seed = 31337 },
                    cancellationToken));

            return exception.ToString().ReplaceLineEndings("\n");
        }
    }

    /// <summary>Verifies schedule mismatch reports include blocked probe details.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScheduleMismatchShowsBlockedWorkerProbe(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("actual", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 28,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Scripted schedule step 1");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("expected");
        _ = await Assert.That(report).Contains("actual");
    }

    /// <summary>Verifies schedule mismatch traces include all blocked workers.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScheduleMismatchShowsBlockedWorkersTrace(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("actual-a", cancellationToken));
                    context.Fork(async () => await Probe.HitAsync("actual-b", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 29,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker-1 hit actual-a");
        _ = await Assert.That(report).Contains("worker-2 hit actual-b");
        _ = await Assert.That(report).Contains("expected");
    }

    /// <summary>Verifies timeout reports still include forked workers before probes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TimeoutBeforeAnyProbeReportsForked(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var run = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await gate.Task.WaitAsync(cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 31, Timeout = TimeSpan.FromMilliseconds(50) },
                cancellationToken);

            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(run);

            var report = exception.ToString();
            _ = await Assert.That(report).Contains("worker-1 forked");
            _ = await Assert.That(report).Contains("braid run timed out.");
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies timeout reports include blocked worker trace entries.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TimeoutReportIncludesBlockedWorkers(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var run = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("a", cancellationToken);
                        await gate.Task.WaitAsync(cancellationToken);
                    });
                    context.Fork(async () => await Probe.HitAsync("b", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 30, Timeout = TimeSpan.FromMilliseconds(50) },
                cancellationToken);

            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(run);

            var report = exception.ToString();
            _ = await Assert.That(report).Contains("braid run timed out.");
            _ = await Assert.That(report).Contains("worker-1 hit a");
            _ = await Assert.That(report).Contains("worker-2 hit b");
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies trace data does not leak across iterations.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceDoesNotLeakAcrossIterations(CancellationToken cancellationToken)
    {
        var calls = 0;
        var run = Runner.RunAsync(
            async context =>
            {
                var invocation = Interlocked.Increment(ref calls);
                context.Fork(async () => await Probe.HitAsync($"iteration-{invocation}", cancellationToken));
                await context.JoinAsync(cancellationToken);
                if (invocation == 3)
                    throw new InvalidOperationException("fail on third invocation");
            },
            new RunOptions { Iterations = 3, Seed = 150 },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(run);

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("iteration-3");
        _ = await Assert.That(report).DoesNotContain("iteration-1");
        _ = await Assert.That(report).DoesNotContain("iteration-2");
    }
}
