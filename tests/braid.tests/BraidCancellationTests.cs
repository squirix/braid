namespace Braid.Tests;

/// <summary>Covers cancellation and timeout behavior.</summary>
public sealed class BraidCancellationTests : TestBase
{
    /// <summary>Verifies timeout failures are reported as braid run exceptions.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncReportsTimeoutAsRunException(CancellationToken cancellationToken)
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var operation = Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false));

                    await context.JoinAsync(cancellationToken);
                },
                options,
                cancellationToken);
            var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

            var report = exception.ToString();
            _ = await Assert.That(report).Contains("braid run timed out.");
            _ = await Assert.That(report).Contains("Seed: 12345");
            _ = await Assert.That(report).Contains("Trace:");
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies external cancellation unblocks a waiting braid run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task RunAsyncSurfacesCanceledExternally(CancellationToken cancellationToken) => BraidAssertions.AssertExpectsAnyAsync<OperationCanceledException, CancellationToken>(cancellationToken, static state => RunAndCancelExternallyAsync(state));

    private static async Task RunAndCancelExternallyAsync(CancellationToken cancellationToken = default)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(30));
        var externalToken = cancellation.Token;
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromSeconds(5),
            Schedule = ReplaySchedule.Replay(new ReplayStep("worker-2", "ready")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                context.Fork(async () =>
                {
                    while (!externalToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, externalToken).ConfigureAwait(false);
                });

                await context.JoinAsync(externalToken);
            },
            options,
            externalToken);
    }
}
