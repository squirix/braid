using Xunit;

namespace Braid.Tests;

/// <summary>Covers exceptionandtracereport behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidExceptionAndTraceReportTests : TestBase
{
    /// <summary>Verifies exception formatting succeeds for empty trace and empty schedule.</summary>
    [Fact]
    public void RunExceptionToStringEmptyTraceSchedule()
    {
        var ex = new BraidRunException("message", 7, 3, [], [], null);
        var report = ex.ToString();
        Assert.Contains("message", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 7", report, StringComparison.Ordinal);
        Assert.Contains("Iteration: 3", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies exception formatting handles null inner exception deterministically.</summary>
    [Fact]
    public void RunExceptionToStringNullInnerException()
    {
        var ex = new BraidRunException("message", 17, 1, ["worker-1 forked"], [new BraidStep("worker-1", "ready")], null);
        var first = ex.ToString();
        var second = ex.ToString();
        Assert.DoesNotContain("Inner exception:", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    /// <summary>Verifies callback failure without schedule does not print schedule section.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureWithoutScheduleNoSection()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () => await BraidRunner.RunAsync(
            static _ => throw new InvalidOperationException("callback-failed"),
            DefaultCancellationToken));

        var report = exception.ToString();
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies callback failure with schedule prints schedule section.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureWithSchedulePrintsSection()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static _ => throw new InvalidOperationException("callback-failed"),
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4009,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ ready", report, StringComparison.Ordinal);
        Assert.Contains("callback-failed", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies empty replay schedule failure does not print schedule entries.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EmptyReplayScheduleDoesNotPrintEntries()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("worker-failed")));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9015,
                    Schedule = BraidSchedule.Replay(),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.DoesNotContain("Schedule:", report, StringComparison.Ordinal);
        Assert.Contains("worker-failed", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies each worker completion appears exactly once in trace.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceContainsSingleCompletionPerWorker()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4002,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready"), new BraidStep("worker-2", "ready")),
                },
                DefaultCancellationToken);
        });

        Assert.Equal(1, CountContains(exception.Trace, "worker-1 completed"));
        Assert.Equal(1, CountContains(exception.Trace, "worker-2 completed"));
    }

    /// <summary>Verifies probe release entries appear only after matching probe hits.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceDoesNotReleaseProbeBeforeHit()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4003,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        AssertAppearsBefore(exception.Trace, "worker-1 hit ready", "worker-1 released at ready");
    }

    /// <summary>Verifies trace entries preserve expected fork/release/hit/complete relative order.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TraceOrdersForkReleaseHitCompleteEvents()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4001,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready"), new BraidStep("worker-1", "ready")),
                },
                DefaultCancellationToken);
        });

        var trace = exception.Trace;
        AssertAppearsBefore(trace, "worker-1 forked", "worker-1 hit ready");
        AssertAppearsBefore(trace, "worker-2 forked", "worker-2 hit ready");
        AssertAppearsBefore(trace, "worker-2 released at ready", "worker-1 released at ready");
        AssertAppearsBefore(trace, "worker-1 released at ready", "worker-1 completed");
        AssertAppearsBefore(trace, "worker-2 released at ready", "worker-2 completed");
    }

    /// <summary>Verifies wrong probe diagnostics mention expected and actual probe.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WrongProbeNameCorrectWorkerReportsProbe()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4007,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
        Assert.Contains("worker-1", report, StringComparison.Ordinal);
        Assert.Contains("expected", report, StringComparison.Ordinal);
        Assert.Contains("actual", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies wrong worker diagnostics mention expected worker and blocked actual worker.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WrongWorkerCorrectProbeReportsBlocked()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4008,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("worker-2 @ ready", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 hit ready", report, StringComparison.Ordinal);
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies mismatch reports keep the failing schedule step and trace details.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ScheduleMismatchNoAdvanceBeforeFailure()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("actual", DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4006,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "expected")),
                },
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("Scripted schedule step 0", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 @ expected", report, StringComparison.Ordinal);
        Assert.Contains("worker-1 hit actual", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies long probe names can be reported without crashes.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task LongProbeNameIsReportedWithoutCrashing()
    {
        var probeName = new string('x', 512);
        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await BraidProbe.HitAsync(probeName, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9013,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", probeName)),
                },
                DefaultCancellationToken);
        });

        Assert.Contains(probeName, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies valid punctuation probe names are accepted and reported.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ProbeNameAllowsNonWhitespaceNames()
    {
        const string probeName = "phase:read/write#1";
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync(probeName, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 9010,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", probeName)),
                },
                DefaultCancellationToken);
        });

        Assert.Contains(probeName, exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies repeated probe hits generate repeated hit/release entries.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RepeatedProbeHitsRepeatedTraceEntries()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                        await BraidProbe.HitAsync("loop", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new BraidOptions
                {
                    Iterations = 1,
                    Seed = 4005,
                    Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "loop"), new BraidStep("worker-1", "loop"), new BraidStep("worker-1", "loop")),
                },
                DefaultCancellationToken);
        });

        Assert.Equal(3, CountContains(exception.Trace, "worker-1 hit loop"));
        Assert.Equal(3, CountContains(exception.Trace, "worker-1 released at loop"));
    }

    private static int CountContains(IReadOnlyList<string> trace, string contains)
    {
        var count = 0;
        for (var index = 0; index < trace.Count; index++)
            if (trace[index].Contains(contains, StringComparison.Ordinal))
                count++;

        return count;
    }

    private static int IndexOfContains(IReadOnlyList<string> trace, string contains)
    {
        for (var i = 0; i < trace.Count; i++)
            if (trace[i].Contains(contains, StringComparison.Ordinal))
                return i;

        return -1;
    }

    private static void AssertAppearsBefore(IReadOnlyList<string> trace, string first, string second)
    {
        var firstIndex = IndexOfContains(trace, first);
        var secondIndex = IndexOfContains(trace, second);
        Assert.True(firstIndex >= 0, $"Could not find trace entry containing '{first}'.");
        Assert.True(secondIndex >= 0, $"Could not find trace entry containing '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}', but got indexes {firstIndex} and {secondIndex}.");
    }
}
