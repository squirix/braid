using Xunit;

namespace Braid.Tests;

/// <summary>Covers how callback faults and cancellations are surfaced as run failures.</summary>
public sealed class BraidCallbackFaultReportingTests : TestBase
{
    /// <summary>Verifies callback failures are not masked by non-cooperative workers during stop.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailureNotMaskedDuringStop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = Assertions.ExpectsAsync<BraidRunException>(
                BraidRunner.RunAsync(
                    context =>
                    {
                        context.Fork(async () =>
                        {
                            await BraidProbe.HitAsync("ready", DefaultCancellationToken);
                            await gate.Task.WaitAsync(DefaultCancellationToken);
                        });

                        throw new InvalidOperationException("callback boom");
                    },
                    new BraidOptions { Iterations = 1, Seed = 5101 },
                    DefaultCancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Run should fail quickly with callback failure.", TimeSpan.FromSeconds(3), false);
            var exception = await exceptionTask;
            Assert.Contains("callback boom", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies callback faulted task is surfaced as callback failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncCallbackFaultedTaskIsReported()
    {
        var exception = await Assertions.ExpectsAsync<BraidRunException>(
            BraidRunner.RunAsync(static _ => Task.FromException(new InvalidOperationException("callback faulted")), DefaultCancellationToken));

        Assert.Contains("callback faulted", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Verifies callback canceled task with run token surfaces operation canceled.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunCallbackCanceledTaskSurfacesCanceled()
    {
        using var cts = new CancellationTokenSource();
        _ = await Assertions.ExpectsAnyAsync<OperationCanceledException>(
            BraidRunner.RunAsync(
                async _ =>
                {
                    await cts.CancelAsync();
                    await Task.FromCanceled(cts.Token);
                },
                cts.Token));
    }

    /// <summary>Verifies callback canceled task with unrelated token is treated as callback failure.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunCallbackCanceledUnrelatedAsFailure()
    {
        var cancellationToken = new CancellationToken(true);
        var exception = await Assertions.ExpectsAsync<BraidRunException>(BraidRunner.RunAsync(_ => Task.FromCanceled(cancellationToken), DefaultCancellationToken));

        Assert.True(exception.InnerException is OperationCanceledException);
        Assert.Contains("braid run failed.", exception.Message, StringComparison.Ordinal);
    }
}
