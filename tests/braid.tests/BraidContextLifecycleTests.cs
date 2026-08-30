using Xunit;

namespace Braid.Tests;

/// <summary>Covers context lifecycle behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidContextLifecycleTests : TestBase
{
    /// <summary>Verifies context use after failed completion fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterFailedRunFailsClearly()
    {
        BraidContext? capturedContext = null;
        _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
        {
            await BraidRunner.RunAsync(
                context =>
                {
                    capturedContext = context;
                    throw new InvalidOperationException("callback failed");
                },
                DefaultCancellationToken);
        });

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies context use after successful completion fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterSuccessfulRunFailsClearly()
    {
        BraidContext? capturedContext = null;
        await BraidRunner.RunAsync(
            context =>
            {
                capturedContext = context;
                return Task.CompletedTask;
            },
            DefaultCancellationToken);

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies context use after timeout fails clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ContextUseAfterTimedOutRunFailsClearly()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BraidContext? capturedContext = null;

        try
        {
            _ = await Assert.ThrowsAsync<BraidRunException>(async () =>
            {
                await BraidRunner.RunAsync(
                    async context =>
                    {
                        capturedContext = context;
                        context.Fork(async () => await gate.Task.WaitAsync(DefaultCancellationToken));
                        await context.JoinAsync(DefaultCancellationToken);
                    },
                    new BraidOptions { Iterations = 1, Seed = 444, Timeout = TimeSpan.FromMilliseconds(50) },
                    DefaultCancellationToken);
            });
        }
        finally
        {
            _ = gate.TrySetResult();
        }

        Assert.NotNull(capturedContext);
        var context = capturedContext;
        var forkException = Assert.ThrowsAny<Exception>(() => context.Fork(static () => Task.CompletedTask));
        Assert.True(
            forkException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {forkException.GetType().FullName}: {forkException.Message}");

        var joinException = await Assert.ThrowsAnyAsync<Exception>(() => context.JoinAsync(DefaultCancellationToken));
        Assert.True(
            joinException is InvalidOperationException or BraidRunException,
            $"Expected clear context-lifecycle failure. Got {joinException.GetType().FullName}: {joinException.Message}");
    }

    /// <summary>Verifies nested braid runs are rejected from the run callback before any fork.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task NestedRunInRunCallbackNoLeakScope()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    _ = await Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                    {
                        await BraidRunner.RunAsync(
                            static inner =>
                            {
                                _ = inner;
                                return Task.CompletedTask;
                            },
                            new BraidOptions { Iterations = 1, Seed = 777 },
                            DefaultCancellationToken);
                    });

                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Nested run in callback should fail before corrupting outer scope.");
    }

    /// <summary>Verifies nested braid runs are rejected and do not corrupt the outer scope.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task NestedRunInWorkerNoLeakOuterRun()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () =>
                    {
                        await BraidProbe.HitAsync("outer-before-nested", DefaultCancellationToken);
                        _ = await Assert.ThrowsAsync<InvalidOperationException>(static async () =>
                        {
                            await BraidRunner.RunAsync(
                                static async inner =>
                                {
                                    inner.Fork(static async () => await BraidProbe.HitAsync("inner-ready", DefaultCancellationToken));

                                    await inner.JoinAsync(DefaultCancellationToken);
                                },
                                new BraidOptions { Iterations = 1, Seed = 999 },
                                DefaultCancellationToken);
                        });

                        await BraidProbe.HitAsync("outer-after-nested", DefaultCancellationToken);
                    });

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Nested run inside worker should fail clearly without corrupting outer run.");
    }

    /// <summary>Verifies two concurrent JoinAsync calls from the same callback either both complete or fail clearly.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task ConcurrentJoinAsyncNoCorruptScheduler()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    var join1 = context.JoinAsync(DefaultCancellationToken);
                    var join2 = context.JoinAsync(DefaultCancellationToken);
                    await Task.WhenAll(join1, join2);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Concurrent JoinAsync should not deadlock or surface SemaphoreFullException.");
    }

    /// <summary>Verifies a second JoinAsync after the first completed join is idempotent for a simple completed run.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task JoinAsyncCalledTwiceAfterCompletion()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Sequential second JoinAsync should not deadlock or throw SemaphoreFullException.");
    }

    /// <summary>Verifies BraidRunner.RunAsync joins forked workers after the callback returns without an explicit join.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task RunAsyncAutoJoinsWithoutExplicitJoin()
    {
        var completed = 0;

        await AssertCompletesBeforeWatchdogAsync(
            () => BraidRunner.RunAsync(
                context =>
                {
                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("a", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });

                    context.Fork(async () =>
                    {
                        await BraidProbe.HitAsync("b", DefaultCancellationToken);
                        _ = Interlocked.Increment(ref completed);
                    });

                    return Task.CompletedTask;
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Auto-join after callback should complete both workers.");

        Assert.Equal(2, completed);
    }

    /// <summary>Verifies user JoinAsync plus outer RunAsync join does not double-release workers or throw semaphore errors.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task RunAsyncDoesNotReleaseTwiceAfterJoin()
    {
        return AssertCompletesBeforeWatchdogAsync(
            static () => BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static async () => await BraidProbe.HitAsync("ready", DefaultCancellationToken));

                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 12345 },
                DefaultCancellationToken),
            "Double join should complete without SemaphoreFullException or BraidRunException.");
    }
}
