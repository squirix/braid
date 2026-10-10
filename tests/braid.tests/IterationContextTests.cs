namespace Braid.Tests;

/// <summary>Covers where the run callback starts: on the thread pool, without the synchronization context of the caller.</summary>
public sealed class IterationContextTests : TestBase
{
    private const string CallerContext = "caller";
    private const string NoContext = "none";

    /// <summary>Verifies no iteration starts its callback on the synchronization context of the caller, the first one included.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task NoIterationStartsOnCallerContext(CancellationToken cancellationToken)
    {
        var callerContext = new SynchronizationContext();
        var starts = new List<string>();

        await StartOnContextAsync(
            callerContext,
            () => Runner.RunAsync(
                async context =>
                {
                    starts.Add(DescribeCurrentContext(callerContext));
                    context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 3, Seed = 1 },
                cancellationToken));

        _ = await Assert.That(string.Join(',', starts)).IsEqualTo("none,none,none");
    }

    /// <summary>Verifies neither the discovery run nor a generated schedule starts the callback on the synchronization context of the caller.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task NoExploreRunStartsOnCallerContext(CancellationToken cancellationToken)
    {
        var callerContext = new SynchronizationContext();
        var starts = new List<string>();

        await StartOnContextAsync(
            callerContext,
            () => Runner.ExploreAsync(
                static options => options.WithSeed(1).WithMaxSchedules(10),
                async braid =>
                {
                    starts.Add(DescribeCurrentContext(callerContext));
                    await braid.WorkerAsync("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    await braid.WorkerAsync("w2", async () => await Probe.HitAsync("b", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(starts.Count).IsGreaterThan(1);
        _ = await Assert.That(starts).DoesNotContain(CallerContext);
    }

    /// <summary>Verifies a caller that blocks the only thread of its synchronization context while it waits for the run does not hang the run.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task BlockedCallerContextDoesNotHangTheRun(CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
            var run = Runner.RunAsync(
                async context =>
                {
                    context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 3, Seed = 1 },
                cancellationToken);
            IAsyncResult result = run;
            completed.SetResult(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)) && run.IsCompletedSuccessfully);
        })
        {
            IsBackground = true,
        };
        thread.Start();

        _ = await Assert.That(await completed.Task.WaitAsync(cancellationToken)).IsTrue();
    }

    private static string DescribeCurrentContext(SynchronizationContext callerContext) =>
        ReferenceEquals(SynchronizationContext.Current, callerContext) ? CallerContext : NoContext;

    private static Task StartOnContextAsync(SynchronizationContext context, Func<Task> start)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        Task started;
        try
        {
            started = start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return started;
    }

    private sealed class NeverPumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // The only thread of this context is blocked by the caller, so a posted continuation never runs.
        }
    }
}
