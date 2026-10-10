namespace Braid.Tests;

/// <summary>Covers where the run callback starts: on the thread pool, without the synchronization context or task scheduler of the caller.</summary>
public sealed class IterationContextTests : TestBase
{
    private const string Pool = "pool";

    /// <summary>Verifies every iteration starts its callback on the thread pool when the caller has a synchronization context, the first one included.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task EveryIterationStartsOnThePool(CancellationToken cancellationToken)
    {
        var starts = new List<string>();

        await StartOnContextAsync(
            new PostingContext(),
            () => Runner.RunAsync(
                async context =>
                {
                    starts.Add(DescribeStart());
                    context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 3, Seed = 1 },
                cancellationToken));

        _ = await Assert.That(string.Join(',', starts)).IsEqualTo("pool,pool,pool");
    }

    /// <summary>Verifies every iteration starts its callback on the default task scheduler when the caller runs on another one.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task EveryIterationStartsOnDefaultScheduler(CancellationToken cancellationToken)
    {
        var starts = new List<string>();
        var callerScheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

        await Task.Factory.StartNew(
            () => Runner.RunAsync(
                async context =>
                {
                    starts.Add(DescribeStart());
                    context.Fork("w", async () => await Probe.HitAsync("a", cancellationToken));
                    await context.JoinAsync(cancellationToken);
                },
                new RunOptions { Iterations = 3, Seed = 1 },
                cancellationToken),
            cancellationToken,
            TaskCreationOptions.DenyChildAttach,
            callerScheduler).Unwrap();

        _ = await Assert.That(string.Join(',', starts)).IsEqualTo("pool,pool,pool");
    }

    /// <summary>Verifies every run of an exploration starts the callback on the thread pool.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task EveryExploreRunStartsOnThePool(CancellationToken cancellationToken)
    {
        var starts = new List<string>();

        await StartOnContextAsync(
            new PostingContext(),
            () => Runner.ExploreAsync(
                static options => options.WithSeed(1).WithMaxSchedules(10),
                async braid =>
                {
                    starts.Add(DescribeStart());
                    await braid.WorkerAsync("w1", async () => await Probe.HitAsync("a", cancellationToken));
                    await braid.WorkerAsync("w2", async () => await Probe.HitAsync("b", cancellationToken));
                    await braid.JoinAsync(cancellationToken);
                },
                cancellationToken));

        _ = await Assert.That(starts.Count).IsGreaterThan(1);
        _ = await Assert.That(starts.TrueForAll(static start => string.Equals(start, Pool, StringComparison.Ordinal))).IsTrue();
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
            try
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
                _ = completed.TrySetResult(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)) && run.IsCompletedSuccessfully);
            }
            catch (OperationCanceledException ex)
            {
                // A canceled test token makes RunAsync throw here; unhandled on this thread, it would end the test process.
                _ = completed.TrySetCanceled(ex.CancellationToken);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        _ = await Assert.That(await completed.Task.WaitAsync(cancellationToken)).IsTrue();
    }

    private static string DescribeStart()
    {
        return (SynchronizationContext.Current, TaskScheduler.Current == TaskScheduler.Default, Thread.CurrentThread.IsThreadPoolThread) switch
        {
            (not null, _, _) => "context",
            (null, false, _) => "scheduler",
            (null, true, false) => "thread",
            (null, true, true) => Pool,
        };
    }

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

    private sealed class PostingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            _ = ThreadPool.QueueUserWorkItem(
                _ =>
                {
                    var previous = Current;
                    SetSynchronizationContext(this);
                    try
                    {
                        d(state);
                    }
                    finally
                    {
                        SetSynchronizationContext(previous);
                    }
                });
        }
    }
}
