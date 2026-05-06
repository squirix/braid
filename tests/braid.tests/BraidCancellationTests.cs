using Xunit;

namespace Braid.Tests;

/// <summary>
/// Covers cancellation and timeout behavior.
/// </summary>
public sealed class BraidCancellationTests : TestBase
{
    /// <summary>
    /// Verifies timeout failures are reported as braid run exceptions.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncReportsTimeoutAsBraidRunException()
    {
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var exception = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await Braid.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => { await Task.Delay(TimeSpan.FromMilliseconds(200), DefaultCancellationToken); });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                options,
                DefaultCancellationToken);
        });

        var report = exception.ToString();
        Assert.Contains("braid run timed out.", report, StringComparison.Ordinal);
        Assert.Contains("Seed: 12345", report, StringComparison.Ordinal);
        Assert.Contains("Trace:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies external cancellation unblocks a waiting braid run.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncSurfacesOperationCanceledWhenCanceledExternally()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new BraidOptions
        {
            Iterations = 1,
            Seed = 12345,
            Timeout = TimeSpan.FromSeconds(5),
            Schedule = BraidSchedule.Replay(new BraidStep("worker-2", "ready")),
        };

        var runTask = Braid.RunAsync(
            async context =>
            {
                context.Fork(static async () => { await BraidProbe.HitAsync("ready", DefaultCancellationToken); });

                context.Fork(async () =>
                {
                    while (!cancellation.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellation.Token).ConfigureAwait(false);
                    }
                });

                await context.JoinAsync(cancellation.Token);
            },
            options,
            cancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(30), DefaultCancellationToken);
        cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runTask);
    }
}
