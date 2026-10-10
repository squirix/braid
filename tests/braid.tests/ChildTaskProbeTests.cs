namespace Braid.Tests;

/// <summary>Covers a probe hit from a task that a worker started.</summary>
public sealed class ChildTaskProbeTests : TestBase
{
    /// <summary>Verifies a worker that returns while a task it started still waits at a probe fails with an error that names the probe.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerReturningWithParkedTaskFails(CancellationToken cancellationToken)
    {
        var child = Task.CompletedTask;

        // "other" keeps running until "w" has returned, so the schedule cannot end while the task of "w" is parked and "w" has not returned yet.
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                async context =>
                {
                    context.Fork(
                        "w",
                        async () =>
                        {
                            child = StartNewOnThreadPoolAsync(() => Probe.HitAsync("child", cancellationToken).AsTask(), cancellationToken);
                            await WaitForTraceAsync(context, "w hit child", cancellationToken);
                        });
                    context.Fork("other", () => WaitForTraceAsync(context, "w completed", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 1, Schedule = ReplaySchedule.Replay(ReplayStep.Start("w"), ReplayStep.Start("other")) },
                cancellationToken));

        await child.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _ = await Assert.That(exception.Message).StartsWith("A forked operation failed.");
        _ = await Assert.That(exception.InnerException).IsTypeOf<RunException>();
        _ = await Assert.That(exception.InnerException!.Message).StartsWith(
            "Worker 'w' returned while a task it started was still waiting at probe 'child'. A task that a worker starts may hit a probe only while the worker waits for that task.");
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
