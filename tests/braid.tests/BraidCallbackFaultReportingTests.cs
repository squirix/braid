namespace Braid.Tests;

/// <summary>Covers how callback faults and cancellations are surfaced as run failures.</summary>
public sealed class BraidCallbackFaultReportingTests : TestBase
{
    /// <summary>Verifies callback failures are not masked by non-cooperative workers during stop.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailureNotMaskedDuringStop(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var exceptionTask = BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    context =>
                    {
                        context.Fork(async () =>
                        {
                            await Probe.HitAsync("ready", cancellationToken);
                            await gate.Task.WaitAsync(cancellationToken);
                        });

                        throw new InvalidOperationException("callback boom");
                    },
                    new RunOptions { Iterations = 1, Seed = 5101 },
                    cancellationToken));

            await AssertCompletesBeforeWatchdogAsync(exceptionTask, "Run should fail quickly with callback failure.", TimeSpan.FromSeconds(3), false, cancellationToken);
            var exception = await exceptionTask;
            _ = await Assert.That(exception.ToString()).Contains("callback boom");
        }
        finally
        {
            _ = gate.TrySetResult();
        }
    }

    /// <summary>Verifies callback faulted task is surfaced as callback failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncCallbackFaultedTaskIsReported(CancellationToken cancellationToken)
    {
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(static _ => Task.FromException(new InvalidOperationException("callback faulted")), cancellationToken));

        _ = await Assert.That(exception.ToString()).Contains("callback faulted");
    }

    /// <summary>Verifies callback canceled task with run token surfaces operation canceled.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunCallbackCanceledTaskSurfacesCanceled()
    {
        using var cts = new CancellationTokenSource();
        _ = await BraidAssertions.AssertExpectsAnyAsync<OperationCanceledException, CancellationTokenSource>(
            cts,
            static state => Runner.RunAsync(
                async _ =>
                {
                    await state.CancelAsync();
                    await Task.FromCanceled(state.Token);
                },
                state.Token));
    }

    /// <summary>Verifies callback canceled task with unrelated token is treated as callback failure.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunCallbackCanceledUnrelatedAsFailure(CancellationToken cancellationToken)
    {
        var canceledToken = new CancellationToken(true);
        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(Runner.RunAsync(_ => Task.FromCanceled(canceledToken), cancellationToken));

        _ = await Assert.That(exception.InnerException is OperationCanceledException).IsTrue();
        _ = await Assert.That(exception.Message).Contains("braid run failed.");
    }
}
