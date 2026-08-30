using System.Runtime.CompilerServices;
using Xunit;

namespace Braid.Tests;

/// <summary>Covers inner exception stack trace behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidInnerExceptionStackTraceTests : TestBase
{
    /// <summary>Verifies callback failures preserve original inner exception stack trace.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CallbackFailurePreservesStackTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () => await BraidRunner.RunAsync(
            static _ => ThrowFromCallbackHelperAsync(),
            DefaultCancellationToken));

        Assert.NotNull(exception.InnerException);
        Assert.Contains(nameof(ThrowFromCallbackHelperAsync), exception.InnerException.StackTrace ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Verifies worker failures preserve original inner exception stack trace.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task WorkerFailurePreservesStackTrace()
    {
        var exception = await Assert.ThrowsAsync<BraidRunException>(static async () =>
        {
            await BraidRunner.RunAsync(
                static async context =>
                {
                    context.Fork(static () => StartNewOnThreadPoolAsync(ThrowFromWorkerHelper, DefaultCancellationToken));
                    await context.JoinAsync(DefaultCancellationToken);
                },
                new BraidOptions { Iterations = 1, Seed = 4010 },
                DefaultCancellationToken);
        });

        Assert.NotNull(exception.InnerException);
        Assert.Contains(nameof(ThrowFromWorkerHelper), exception.InnerException.StackTrace ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), exception.ToString(), StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task ThrowFromCallbackHelperAsync()
    {
        ThrowFromCallbackHelperCore();
        return Task.CompletedTask;
    }

    private static void ThrowFromCallbackHelperCore() => throw new InvalidOperationException("callback-helper-failure");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromWorkerHelper() => ThrowFromWorkerHelperCore();

    private static void ThrowFromWorkerHelperCore() => throw new InvalidOperationException("worker-helper-failure");
}
