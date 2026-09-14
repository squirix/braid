namespace Braid.Tests;

/// <summary>Covers exception and trace report behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidExceptionAndTraceReportTests : TestBase
{
    /// <summary>Verifies callback failure with schedule prints schedule section.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureWithSchedulePrintsSection(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                static _ => throw new InvalidOperationException("callback-failed"),
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4009,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Schedule:");
        _ = await Assert.That(report).Contains("worker-1 @ ready");
        _ = await Assert.That(report).Contains("callback-failed");
    }

    /// <summary>Verifies callback failure without schedule does not print schedule section.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureWithoutScheduleNoSection(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(static _ => throw new InvalidOperationException("callback-failed"), cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Trace:");
        _ = await Assert.That(report).DoesNotContain("Schedule:");
    }

    /// <summary>Verifies empty replay schedule failure does not print schedule entries.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task EmptyReplayScheduleDoesNotPrintEntries(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(static () => Task.FromException(new InvalidOperationException("worker-failed")));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9015,
                    Schedule = ReplaySchedule.Replay(),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).DoesNotContain("Schedule:");
        _ = await Assert.That(report).Contains("worker-failed");
    }

    /// <summary>Verifies long probe names can be reported without crashes.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task LongProbeNameIsReportedWithoutCrashing(CancellationToken cancellationToken)
    {
        var probeName = new string('x', 512);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync(probeName, cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9013,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", probeName)),
                },
                cancellationToken));

        _ = await Assert.That(exception.ToString()).Contains(probeName);
    }

    /// <summary>Verifies valid punctuation probe names are accepted and reported.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ProbeNameAllowsNonWhitespaceNames(CancellationToken cancellationToken)
    {
        const string probeName = "phase:read/write#1";
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync(probeName, cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 9010,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", probeName)),
                },
                cancellationToken));

        _ = await Assert.That(exception.ToString()).Contains(probeName);
    }

    /// <summary>Verifies repeated probe hits generate repeated hit/release entries.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RepeatedProbeHitsRepeatedTraceEntries(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("loop", cancellationToken);
                        await Probe.HitAsync("loop", cancellationToken);
                        await Probe.HitAsync("loop", cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4005,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "loop"), new ReplayStep("worker-1", "loop"), new ReplayStep("worker-1", "loop")),
                },
                cancellationToken));

        _ = await Assert.That(CountContains(exception.Traces, "worker-1 hit loop")).IsEqualTo(3);
        _ = await Assert.That(CountContains(exception.Traces, "worker-1 released at loop")).IsEqualTo(3);
    }

    /// <summary>Verifies exception formatting succeeds for empty trace and empty schedule.</summary>
    [Test]
    public async Task RunExceptionToStringEmptyTraceSchedule()
    {
        var ex = new RunException("message", 7, 3, [], [], null);
        var report = ex.ToString();
        _ = await Assert.That(report).Contains("message");
        _ = await Assert.That(report).Contains("Seed: 7");
        _ = await Assert.That(report).Contains("Iteration: 3");
        _ = await Assert.That(report).Contains("Trace:");
    }

    /// <summary>Verifies exception formatting handles null inner exception deterministically.</summary>
    [Test]
    public async Task RunExceptionToStringNullInnerException()
    {
        var ex = new RunException("message", 17, 1, ["worker-1 forked"], [new ReplayStep("worker-1", "ready")], null);
        var first = ex.ToString();
        var second = ex.ToString();
        _ = await Assert.That(first).DoesNotContain("Inner exception:");
        _ = await Assert.That(second).IsEqualTo(first);
    }

    /// <summary>Verifies mismatch reports keep the failing schedule step and trace details.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ScheduleMismatchNoAdvanceBeforeFailure(CancellationToken cancellationToken)
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
                    Seed = 4006,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "expected")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Scripted schedule step 1");
        _ = await Assert.That(report).Contains("worker-1 @ expected");
        _ = await Assert.That(report).Contains("worker-1 hit actual");
    }

    /// <summary>Verifies each worker completion appears exactly once in trace.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceContainsSingleCompletionPerWorker(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4002,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready"), new ReplayStep("worker-2", "ready")),
                },
                cancellationToken));

        _ = await Assert.That(CountContains(exception.Traces, "worker-1 completed")).IsEqualTo(1);
        _ = await Assert.That(CountContains(exception.Traces, "worker-2 completed")).IsEqualTo(1);
    }

    /// <summary>Verifies probe release entries appear only after matching probe hits.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceDoesNotReleaseProbeBeforeHit(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4003,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "ready")),
                },
                cancellationToken));

        await AssertAppearsBeforeAsync(exception.Traces, "worker-1 hit ready", "worker-1 released at ready");
    }

    /// <summary>Verifies trace entries preserve expected fork/release/hit/complete relative order.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task TraceOrdersForkReleaseHitCompleteEvents(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                    throw new InvalidOperationException("fail-after-join");
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4001,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready"), new ReplayStep("worker-1", "ready")),
                },
                cancellationToken));

        var trace = exception.Traces;
        await AssertAppearsBeforeAsync(trace, "worker-1 forked", "worker-1 hit ready");
        await AssertAppearsBeforeAsync(trace, "worker-2 forked", "worker-2 hit ready");
        await AssertAppearsBeforeAsync(trace, "worker-2 released at ready", "worker-1 released at ready");
        await AssertAppearsBeforeAsync(trace, "worker-1 released at ready", "worker-1 completed");
        await AssertAppearsBeforeAsync(trace, "worker-2 released at ready", "worker-2 completed");
    }

    /// <summary>Verifies wrong probe diagnostics mention expected and actual probe.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WrongProbeNameCorrectWorkerReportsProbe(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("probe-b", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4007,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-1", "probe-a")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("Scripted schedule step 1");
        _ = await Assert.That(report).Contains("worker-1");
        _ = await Assert.That(report).Contains("probe-a");
        _ = await Assert.That(report).Contains("actual probe is probe-b");
    }

    /// <summary>Verifies wrong worker diagnostics mention expected worker and blocked actual worker.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WrongWorkerCorrectProbeReportsBlocked(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions
                {
                    Iterations = 1,
                    Seed = 4008,
                    Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready")),
                },
                cancellationToken));

        var report = exception.ToString();
        _ = await Assert.That(report).Contains("worker-2 @ ready");
        _ = await Assert.That(report).Contains("worker-1 hit ready");
        _ = await Assert.That(report).Contains("Scripted schedule step 1");
    }

    private static async Task AssertAppearsBeforeAsync(IReadOnlyList<string> trace, string first, string second)
    {
        var firstIndex = IndexOfContains(trace, first);
        var secondIndex = IndexOfContains(trace, second);
        _ = await Assert.That(firstIndex >= 0).IsTrue().Because($"Could not find trace entry containing '{first}'.");
        _ = await Assert.That(secondIndex >= 0).IsTrue().Because($"Could not find trace entry containing '{second}'.");
        _ = await Assert.That(firstIndex < secondIndex).IsTrue().Because($"Expected '{first}' before '{second}', but got indexes {firstIndex} and {secondIndex}.");
    }

    private static int CountContains(IReadOnlyList<string> trace, string contains)
    {
        var count = 0;
        for (var index = 0; index < trace.Count; index++)
        {
            if (trace[index].Contains(contains, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static int IndexOfContains(IReadOnlyList<string> trace, string contains)
    {
        for (var i = 0; i < trace.Count; i++)
        {
            if (trace[i].Contains(contains, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
