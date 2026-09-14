namespace Braid.Tests;

/// <summary>Covers context lifecycle behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidContextLifecycleTests : TestBase
{
    /// <summary>Verifies two concurrent JoinAsync calls from the same callback either both complete or fail clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task ConcurrentJoinAsyncNoCorruptScheduler(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    var join1 = context.JoinAsync(cancellationToken);
                    var join2 = context.JoinAsync(cancellationToken);
                    await Task.WhenAll(join1, join2);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Concurrent JoinAsync should not deadlock or surface SemaphoreFullException.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies context use after failed completion fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ContextUseAfterFailedRunFailsClearly(CancellationToken cancellationToken)
    {
        RunContext? capturedContext = null;
        _ = await BraidAssertions.AssertExpectsAsync<RunException>(
            Runner.RunAsync(
                context =>
                {
                    capturedContext = context;
                    throw new InvalidOperationException("callback failed");
                },
                cancellationToken));

        _ = await Assert.That(capturedContext).IsNotNull();
        var context = capturedContext!;
        var forkException = BraidAssertions.AssertExpectsAny<Exception, RunContext>(context, static state => state.Fork(static () => Task.CompletedTask));
        _ = await Assert.That(forkException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await BraidAssertions.AssertExpectsAnyAsync<Exception, (RunContext Context, CancellationToken Token)>(
            (context, cancellationToken),
            static state => state.Context.JoinAsync(state.Token));
        _ = await Assert.That(joinException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies context use after successful completion fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ContextUseAfterSuccessfulRunFailsClearly(CancellationToken cancellationToken)
    {
        RunContext? capturedContext = null;
        await Runner.RunAsync(
            context =>
            {
                capturedContext = context;
                return Task.CompletedTask;
            },
            cancellationToken);

        _ = await Assert.That(capturedContext).IsNotNull();
        var context = capturedContext!;
        var forkException = BraidAssertions.AssertExpectsAny<Exception, RunContext>(context, static state => state.Fork(static () => Task.CompletedTask));
        _ = await Assert.That(forkException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await BraidAssertions.AssertExpectsAnyAsync<Exception, (RunContext Context, CancellationToken Token)>(
            (context, cancellationToken),
            static state => state.Context.JoinAsync(state.Token));
        _ = await Assert.That(joinException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies context use after timeout fails clearly.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ContextUseAfterTimedOutRunFailsClearly(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunContext? capturedContext = null;

        try
        {
            _ = await BraidAssertions.AssertExpectsAsync<RunException>(
                Runner.RunAsync(
                    async context =>
                    {
                        capturedContext = context;
                        context.Fork(async () => await gate.Task.WaitAsync(cancellationToken));
                        await context.JoinAsync(cancellationToken);
                    },
                    new RunOptions { Iterations = 1, Seed = 444, Timeout = TimeSpan.FromMilliseconds(50) },
                    cancellationToken));
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        _ = await Assert.That(capturedContext).IsNotNull();
        var context = capturedContext!;
        var forkException = BraidAssertions.AssertExpectsAny<Exception, RunContext>(context, static state => state.Fork(static () => Task.CompletedTask));
        _ = await Assert.That(forkException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await BraidAssertions.AssertExpectsAnyAsync<Exception, (RunContext Context, CancellationToken Token)>(
            (context, cancellationToken),
            static state => state.Context.JoinAsync(state.Token));
        _ = await Assert.That(joinException is InvalidOperationException or RunException).IsTrue().Because($"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies a second JoinAsync after the first completed join is idempotent for a simple completed run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task JoinAsyncCalledTwiceAfterCompletion(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Sequential second JoinAsync should not deadlock or throw SemaphoreFullException.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies nested braid runs are rejected from the run callback before any fork.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task NestedRunInRunCallbackNoLeakScope(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    _ = BraidAssertions.AssertExpects<InvalidOperationException>(() =>
                    {
                        _ = Runner.RunAsync(
                            static inner =>
                            {
                                _ = inner;
                                return Task.CompletedTask;
                            },
                            new RunOptions { Iterations = 1, Seed = 777 },
                            cancellationToken);
                    });

                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Nested run in callback should fail before corrupting outer scope.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies nested braid runs are rejected and do not corrupt the outer scope.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task NestedRunInWorkerNoLeakOuterRun(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("outer-before-nested", cancellationToken);
                        _ = BraidAssertions.AssertExpects<InvalidOperationException>(() =>
                        {
                            _ = Runner.RunAsync(
                                async inner =>
                                {
                                    inner.Fork(async () => await Probe.HitAsync("inner-ready", cancellationToken));

                                    await inner.JoinAsync(cancellationToken);
                                },
                                new RunOptions { Iterations = 1, Seed = 999 },
                                cancellationToken);
                        });

                        await Probe.HitAsync("outer-after-nested", cancellationToken);
                    });

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Nested run inside worker should fail clearly without corrupting outer run.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Verifies Runner.RunAsync joins forked workers after the callback returns without an explicit join.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task RunAsyncAutoJoinsWithoutExplicitJoin(CancellationToken cancellationToken)
    {
        var completed = 0;

        await AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                context =>
                {
                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("a", cancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });

                    context.Fork(async () =>
                    {
                        await Probe.HitAsync("b", cancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });

                    return Task.CompletedTask;
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Auto-join after callback should complete both workers.",
            cancellationToken: cancellationToken);

        _ = await Assert.That(completed).IsEqualTo(2);
    }

    /// <summary>Verifies user JoinAsync plus outer RunAsync join does not double-release workers or throw semaphore errors.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public Task RunAsyncDoesNotReleaseTwiceAfterJoin(CancellationToken cancellationToken)
    {
        return AssertCompletesBeforeWatchdogAsync(
            () => Runner.RunAsync(
                async context =>
                {
                    context.Fork(async () => await Probe.HitAsync("ready", cancellationToken));

                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 1, Seed = 12345 },
                cancellationToken),
            "Double join should complete without SemaphoreFullException or RunException.",
            cancellationToken: cancellationToken);
    }
}
