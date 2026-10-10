namespace Braid.Tests;

/// <summary>Covers a probe hit from a task that a worker started.</summary>
public sealed class ChildTaskProbeTests : TestBase
{
    private const string LeftBehind =
        "Worker 'w' returned while probe 'child' was still waiting on it. "
        + "A worker must wait for every probe that it, or a task it started, has hit before it returns.";

    /// <summary>Verifies a worker that returns while a task it started still waits at a probe fails with an error that names the probe.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerReturningWithParkedTaskFails(CancellationToken cancellationToken)
    {
        var exception = await RunWorkerWithParkedTaskAsync([ReplayStep.Start("w"), ReplayStep.Start("other")], "w hit child", null, cancellationToken);

        _ = await Assert.That(exception.Message).StartsWith("A forked operation failed.");
        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith(LeftBehind);
    }

    /// <summary>Verifies the same failure when the task of the worker is held at its probe by an arrive step.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerReturningWithHeldTaskFails(CancellationToken cancellationToken)
    {
        var exception = await RunWorkerWithParkedTaskAsync(
            [ReplayStep.Start("w"), ReplayStep.Arrive("w", "child"), ReplayStep.Start("other")],
            "w arrival observed at child (held)",
            null,
            cancellationToken);

        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith(LeftBehind);
    }

    /// <summary>Verifies a worker that throws while its task waits at a probe is reported with its own failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFailureWinsOverParkedTask(CancellationToken cancellationToken)
    {
        var failure = new InvalidOperationException("worker failed");

        var exception = await RunWorkerWithParkedTaskAsync([ReplayStep.Start("w"), ReplayStep.Start("other")], "w hit child", failure, cancellationToken);

        _ = await Assert.That(exception.InnerException).IsSameReferenceAs(failure);
    }

    /// <summary>Verifies a worker that waits for the task it started, while that task waits at a probe, is still allowed.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerWaitingForItsTaskIsAllowed(CancellationToken cancellationToken)
    {
        var trace = string.Empty;

        await Runner.RunAsync(
            async context =>
            {
                context.Fork("w", async () => await StartNewOnThreadPoolAsync(() => Probe.HitAsync("child", cancellationToken).AsTask(), cancellationToken));
                await context.JoinAsync(cancellationToken);
                trace = string.Join(" | ", context.TraceSteps);
            },
            new RunOptions { Iterations = 1, Seed = 1 },
            cancellationToken);

        _ = await Assert.That(trace).IsEqualTo("w forked | w released | w hit child | w released at child | w completed");
    }

    /// <summary>
    /// Runs worker "w", which starts a task that parks at probe "child", waits until the trace shows <paramref name="parkedStep" />, and then
    /// returns or throws without waiting for the task. Worker "other" runs until "w" has completed, so the schedule cannot end before that.
    /// </summary>
    /// <param name="steps">The schedule.</param>
    /// <param name="parkedStep">The trace step that shows the task of "w" is parked.</param>
    /// <param name="failure">The exception "w" throws, or <see langword="null" /> when it returns.</param>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>The failure of the run.</returns>
    private static async Task<RunException> RunWorkerWithParkedTaskAsync(ReplayStep[] steps, string parkedStep, Exception? failure, CancellationToken cancellationToken)
    {
        var child = Task.CompletedTask;

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(
                        "w",
                        async () =>
                        {
                            child = StartNewOnThreadPoolAsync(() => Probe.HitAsync("child", cancellationToken).AsTask(), cancellationToken);
                            await WaitForTraceAsync(context, parkedStep, cancellationToken);
                            if (failure != null)
                                throw failure;
                        });
                    context.Fork("other", () => WaitForTraceAsync(context, "w completed", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Replay(steps) },
                cancellationToken));

        await child.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return exception;
    }

    private static async Task WaitForTraceAsync(RunContext context, string step, CancellationToken cancellationToken)
    {
        while (!HasStep(context.TraceSteps, step))
            await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, cancellationToken);
    }

    private static bool HasStep(IReadOnlyList<string> trace, string step)
    {
        for (var index = 0; index < trace.Count; index++)
        {
            if (string.Equals(trace[index], step, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
