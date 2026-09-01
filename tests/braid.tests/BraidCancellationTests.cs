using Xunit;

namespace Braid.Tests;

/// <summary>Covers cancellation and timeout behavior.</summary>
public sealed class BraidCancellationTests : TestBase
{
    /// <summary>Verifies timeout failures are reported as braid run exceptions.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsTimeoutAsRunException()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var operation = Runner.RunAsync(
            static async context =>
            {
                context.Fork(static async () => await Task.Delay(TimeSpan.FromMilliseconds(200), TimeProvider.System, DefaultCancellationToken));

                await context.JoinAsync(DefaultCancellationToken);
            },
            options,
            DefaultCancellationToken);
        var exception = await Assertions.ExpectsAsync<RunException>(operation);

        var report = exception.ToString();
        Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>Verifies external cancellation unblocks a waiting braid run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncSurfacesCanceledExternally() => _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(RunAndCancelExternallyAsync);

    private static async Task RunAndCancelExternallyAsync()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(30));
        var cancellationToken = cancellation.Token;
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
                context.Fork(static async () => await Probe.HitAsync("ready", DefaultCancellationToken));

                context.Fork(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, cancellationToken).ConfigureAwait(false);
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);
    }
}
