namespace Braid.Tests;

/// <summary>Covers a callback that waits for a forked worker before it joins, while workers start only when the run joins.</summary>
public sealed class CallbackBeforeJoinTests : TestBase
{
    private const string BeforeJoinMessage = "braid run timed out before the callback called JoinAsync.";
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Verifies the run timeout ends a callback that waits for a forked worker before joining.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WaitBeforeJoinTimesOut(CancellationToken cancellationToken)
    {
        using var workerDone = new CancellationTokenSource();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context => WaitForWorkerBeforeJoinAsync(context, workerDone, cancellationToken),
                new RunOptions { Iterations = 1, Seed = 1, Timeout = RunTimeout },
                cancellationToken));

        await workerDone.CancelAsync();

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
        _ = await Assert.That(exception.Message).StartsWith(BeforeJoinMessage);
        _ = await Assert.That(exception.Traces).Contains("w forked");
    }

    /// <summary>Verifies canceling the run token ends a callback that waits for a forked worker before joining.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WaitBeforeJoinIsCanceled(CancellationToken cancellationToken)
    {
        using var workerDone = new CancellationTokenSource();
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(RunTimeout);
        _ = await BraidAssertions.AssertExpectsAsync<OperationCanceledException>(
            Runner.RunAsync(
                context => WaitForWorkerBeforeJoinAsync(context, workerDone, cancellationToken),
                new RunOptions { Iterations = 1, Seed = 1, Timeout = TimeSpan.FromMinutes(1) },
                runCts.Token));

        await workerDone.CancelAsync();
    }

    /// <summary>Verifies exploration fails when its callback waits for a forked worker before joining.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFailsOnWaitBeforeJoin(CancellationToken cancellationToken)
    {
        using var workerDone = new CancellationTokenSource();
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.ExploreAsync(
                static options => options.WithTimeout(RunTimeout),
                async braid =>
                {
                    await braid.WorkerAsync("w", () => SignalAsync(workerDone, cancellationToken));
                    await WaitAsync(workerDone);
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        await workerDone.CancelAsync();

        _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
        _ = await Assert.That(exception.Message).StartsWith(BeforeJoinMessage);
    }

    private static async Task WaitForWorkerBeforeJoinAsync(RunContext context, CancellationTokenSource workerDone, CancellationToken cancellationToken)
    {
        context.Fork("w", () => SignalAsync(workerDone, cancellationToken));
        await WaitAsync(workerDone);
        await context.JoinAsync(cancellationToken);
    }

    private static async Task SignalAsync(CancellationTokenSource workerDone, CancellationToken cancellationToken)
    {
        await Probe.HitAsync("a", cancellationToken);
        await workerDone.CancelAsync();
    }

    private static Task WaitAsync(CancellationTokenSource workerDone) =>
        Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, workerDone.Token).ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
