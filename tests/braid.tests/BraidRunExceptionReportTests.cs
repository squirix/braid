using System.Globalization;
using Xunit;

namespace Braid.Tests;

/// <summary>Covers run exception report behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidRunExceptionReportTests : TestBase
{
    /// <summary>Verifies exception schedule snapshots are immutable for callers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionScheduleCannotBeMutated()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 33,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                DefaultCancellationToken));

        var list = Assert.IsType<IList<ReplayStep>>(exception.Schedule, false);
        _ = Assertions.ExpectsAny<Exception>(() => list[0] = new ReplayStep("worker-9", "changed"));

        Assert.Contains("worker-1 @ expected", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies exception trace snapshots are immutable for callers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task BraidRunExceptionTraceCannotBeMutated()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 34,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                DefaultCancellationToken));

        var traceList = Assert.IsType<IList<string>>(exception.Trace, false);
        _ = Assertions.ExpectsAny<Exception>(() => traceList[0] = "mutated");

        Assert.Contains("worker-1 hit actual", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies callback failures after forking include fork trace entries.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureAfterForkReportsTrace()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));
                    throw new InvalidOperationException("fail-after-fork");
                },
                new RunOptions { Iterations = 1, Seed = 27 },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("fail-after-fork", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies callback failures before forking still report a trace section.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureBeforeForkEmptyTrace()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(static _ => throw new InvalidOperationException("fail-before-fork"), new RunOptions { Iterations = 1, Seed = 26 }, DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("fail-before-fork", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies a worker failure before any probe is surfaced on join with trace context.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsWorkerFailureFirstProbe()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("before-probe failure")));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("before-probe failure", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies exception properties are reflected in formatted reports.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunExceptionPropertiesMatchReport()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 32,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains(exception.Seed.ToString(CultureInfo.InvariantCulture), report, StringComparison.Ordinal);
        Assert.Contains(exception.Iteration.ToString(CultureInfo.InvariantCulture), report, StringComparison.Ordinal);
        for (var index = 0; index < exception.Schedule.Count; index++)
        {
            var step = exception.Schedule[index];
            Assert.Contains($"{step.WorkerId} @ {step.ProbeName}", report, StringComparison.Ordinal);
        }

        for (var index = 0; index < exception.Trace.Count; index++)
        {
            var traceEntry = exception.Trace[index];
            Assert.Contains(traceEntry, report, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies deterministic seeds produce stable failure reports.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task SameSeedLargeScenarioSameReport()
    {
        var first = await RunOnceAsync();
        var second = await RunOnceAsync();
        Assert.Equal(first, second);
        return;

        static async Task<string> RunOnceAsync()
        {
            var exception = await Assertions.ExpectsAsync<RunException>(
                Runner.RunAsync(
                    static async context =>
                    {
                        for (var workerIndex = 0; workerIndex < 4; workerIndex++)
                            ForkWorkerDeterministicProbes(context, workerIndex);

                        await context.JoinAsync(DefaultCancellationToken);
                        throw new InvalidOperationException("forced deterministic callback failure");
                    },
                    new RunOptions { Iterations = 1, Seed = 31337 },
                    DefaultCancellationToken));

            return exception.ToString().ReplaceLineEndings("\n");
        }
    }

    /// <summary>Verifies schedule mismatch reports include blocked probe details.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchShowsBlockedWorkerProbe()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 28,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step 1", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("expected", report, StringComparison.Ordinal);
        Assert.Contains("actual", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies schedule mismatch traces include all blocked workers.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchShowsBlockedWorkersTrace()
    {
        var exception = await Assertions.ExpectsAsync<RunException>(
            Runner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await Probe.HitAsync("actual-a", DefaultCancellationToken));
                    context.Fork(static async () => await Probe.HitAsync("actual-b", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 29,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("worker-1 hit actual-a", report, StringComparison.Ordinal);
        Assert.Contains("worker-2 hit actual-b", report, StringComparison.Ordinal);
        Assert.Contains("expected", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies timeout reports still include forked workers before probes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutBeforeAnyProbeReportsForked()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var run = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 31, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var exception = await Assertions.ExpectsAsync<RunException>(run);

            var report = exception.ToString();
            Assert.Contains("worker-1 forked", report, StringComparison.Ordinal);
            Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies timeout reports include blocked worker trace entries.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TimeoutReportIncludesBlockedWorkers()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var run = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("a", DefaultCancellationToken);
                        await gate.Task.WaitAsync(DefaultCancellationToken);
                    });
                    context.Fork(static async () => await Probe.HitAsync("b", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 30, Timeout = TimeSpan.FromMilliseconds(50) },
                DefaultCancellationToken);

            var exception = await Assertions.ExpectsAsync<RunException>(run);

            var report = exception.ToString();
            Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
            Assert.Contains("worker-1 hit a", report, StringComparison.Ordinal);
            Assert.Contains("worker-2 hit b", report, StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies trace data does not leak across iterations.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceDoesNotLeakAcrossIterations()
    {
        var calls = 0;
        var run = Runner.RunAsync(
            async context =>
            {
                var invocation = Interlocked.Increment(ref calls);
                context.Fork(async () => await Probe.HitAsync($"iteration-{invocation}", DefaultCancellationToken));
                await context.JoinAsync(DefaultCancellationToken);
                if (invocation == 3)
                    throw new InvalidOperationException("fail on third invocation");
            },
            new RunOptions { Iterations = 3, Seed = 150 },
            DefaultCancellationToken);

        var exception = await Assertions.ExpectsAsync<RunException>(run);

        var report = exception.ToString();
        Assert.Contains("iteration-3", report, StringComparison.Ordinal);
        Assert.DoesNotContain("iteration-1", report, StringComparison.Ordinal);
        Assert.DoesNotContain("iteration-2", report, StringComparison.Ordinal);
    }
}
