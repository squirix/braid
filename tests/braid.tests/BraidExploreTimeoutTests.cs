using TUnit.Assertions.Enums;

namespace Braid.Tests;

/// <summary>Covers how exploration reports runs that time out.</summary>
public sealed class BraidExploreTimeoutTests : TestBase
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>Verifies a hang in the discovery run fails exploration with a timeout.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreSurfacesDiscoveryTimeout(CancellationToken cancellationToken)
    {
        using var hang = new CancellationTokenSource();
        try
        {
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.ExploreAsync(
                    static options => options.WithSeed(1).WithTimeout(RunTimeout),
                    async braid =>
                    {
                        await braid.WorkerAsync("w", () => WaitUntilReleasedAsync(hang.Token));
                        await braid.JoinAsync(cancellationToken);
                    },
                    cancellationToken));

            _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
            _ = await Assert.That(exception.Message).Contains("braid run timed out.");
        }
        finally
        {
            await hang.CancelAsync();
        }
    }

    /// <summary>Verifies a hang under one generated schedule fails exploration with a replay token that reproduces the hang.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExploreFindsScheduleTimeout(CancellationToken cancellationToken)
    {
        using var hang = new CancellationTokenSource();
        try
        {
            // "second" waits for "first" after its probe, so releasing "second" first hangs: "first" stays parked at its probe.
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.ExploreAsync(
                    static options => options.WithSeed(1).WithMaxSchedules(10).WithTimeout(RunTimeout),
                    braid => ForkWaitForFirstAsync(braid.WorkerAsync, braid.JoinAsync, hang.Token, cancellationToken),
                    cancellationToken));

            _ = await Assert.That(exception.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
            _ = await Assert.That(exception.TryGetReplayText(out var replayText, out var error)).IsTrue().Because(error!);
            _ = await Assert.That(exception.Steps).IsEquivalentTo([ReplayStep.Hit("second", "b"), ReplayStep.Hit("first", "a")], CollectionOrdering.Matching);

            var replayed = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    context => ForkWaitForFirstAsync(
                        (workerId, operation) =>
                        {
                            context.Fork(workerId, operation);
                            return Task.CompletedTask;
                        },
                        context.JoinAsync,
                        hang.Token,
                        cancellationToken),
                    new RunOptions
                    {
                        Iterations = 1,
                        Seed = 1,
                        Schedule = ReplaySchedule.Parse(replayText),
                        Timeout = RunTimeout,
                    },
                    cancellationToken));

            _ = await Assert.That(replayed.FailureOrigin).IsEqualTo(RunFailureOrigin.Timeout);
        }
        finally
        {
            await hang.CancelAsync();
        }
    }

    private static async Task ForkWaitForFirstAsync(
        Func<string, Func<Task>, Task> forkAsync,
        Func<CancellationToken, Task> joinAsync,
        CancellationToken hang,
        CancellationToken cancellationToken)
    {
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await forkAsync(
            "first",
            async () =>
            {
                await Probe.HitAsync("a", cancellationToken);
                _ = firstDone.TrySetResult();
            });

        await forkAsync(
            "second",
            async () =>
            {
                await Probe.HitAsync("b", cancellationToken);
                try
                {
                    await firstDone.Task.WaitAsync(hang);
                }
                catch (OperationCanceledException) when (hang.IsCancellationRequested)
                {
                    // The test released the hung worker after the run ended.
                }
            });

        await joinAsync(cancellationToken);
    }

    private static async Task WaitUntilReleasedAsync(CancellationToken hang)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, hang);
        }
        catch (OperationCanceledException) when (hang.IsCancellationRequested)
        {
            // The test released the hung worker after the run ended.
        }
    }
}
