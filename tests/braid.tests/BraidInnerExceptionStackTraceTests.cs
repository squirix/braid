using System.Runtime.CompilerServices;

namespace Braid.Tests;

/// <summary>Covers inner exception stack trace behavior of the braid scheduler and run reporting.</summary>
public sealed class BraidInnerExceptionStackTraceTests : TestBase
{
    /// <summary>Verifies callback failures preserve original inner exception stack trace.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task CallbackFailurePreservesStackTrace(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(static _ => ThrowFromCallbackHelperAsync(), cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.InnerException).IsNotNull();
        _ = await Assert.That(exception.InnerException.StackTrace ?? string.Empty).Contains(nameof(ThrowFromCallbackHelperAsync));
    }

    /// <summary>Verifies worker failures preserve the original inner exception stack trace.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task WorkerFailurePreservesStackTrace(CancellationToken cancellationToken)
    {
        var operation = Runner.RunAsync(
            async context =>
            {
                context.Fork(() => StartNewOnThreadPoolAsync(ThrowFromWorkerHelper, cancellationToken));
                await context.JoinAsync(cancellationToken);
            },
            new RunOptions { Iterations = 1, Seed = 4010 },
            cancellationToken);

        var exception = await BraidAssertions.AssertExpectsAsync<RunException>(operation);

        _ = await Assert.That(exception.InnerException).IsNotNull();
        _ = await Assert.That(exception.InnerException.StackTrace ?? string.Empty).Contains(nameof(ThrowFromWorkerHelper));
        _ = await Assert.That(exception.ToString()).Contains(nameof(InvalidOperationException));
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
