namespace Braid;

/// <summary>Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.</summary>
public static class Runner
{
    /// <summary>
    /// Explores bounded replay schedules for the supplied workers and probe points, stopping at the first test failure.
    /// Discovery uses one random run to learn per-worker probe sequences, then tries generated hit schedules up to the configured bounds.
    /// </summary>
    /// <param name="configure">Configures exploration bounds and seed.</param>
    /// <param name="test">The exploration callback.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when exploration finishes without finding a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> or <paramref name="test" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Configured bounds are invalid.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">A test failure was found under a replay schedule or during discovery.</exception>
    public static Task ExploreAsync(Action<ExploreOptionsBuilder> configure, Func<ExploreContext, Task> test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(test);

        var builder = new ExploreOptionsBuilder();
        configure(builder);
        return ExploreAsync(builder.Build(), test, cancellationToken);
    }

    /// <inheritdoc cref="ExploreAsync(Action{ExploreOptionsBuilder}, Func{ExploreContext, Task}, CancellationToken)" />
    public static Task ExploreAsync(ExploreOptions options, Func<ExploreContext, Task> test, CancellationToken cancellationToken) =>
        Explorer.ExploreAsync(options, test, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="RunContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A braid run is already active, or the callback returned a null task.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">A forked worker failed, the run timed out, or scheduling could not satisfy the replay script.</exception>
    public static Task RunAsync(Func<RunContext, Task> test, CancellationToken cancellationToken) => RunAsync(test, null, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="RunContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="options">The run options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A braid run is already active, or the callback returned a null task.</exception>
    /// <exception cref="ArgumentException"><paramref name="options" /> failed validation.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="RunException">A forked worker failed, the run timed out, or scheduling could not satisfy the replay script.</exception>
    public static Task RunAsync(Func<RunContext, Task> test, RunOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (RunScope.CurrentScheduler != null)
            throw new InvalidOperationException("Nested braid runs are not supported.");

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedOptions = options ?? RunOptions.Default;
        resolvedOptions.Validate();

        return RunAsyncCoreAsync(test, resolvedOptions, cancellationToken);
    }

    private static async Task RunAsyncCoreAsync(Func<RunContext, Task> test, RunOptions resolvedOptions, CancellationToken cancellationToken)
    {
        var baseSeed = resolvedOptions.Seed ?? Environment.TickCount;

        for (var iteration = 0; iteration < resolvedOptions.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seed = unchecked(baseSeed + iteration);
            using var scheduler = new Scheduler(seed, iteration, resolvedOptions.Timeout, resolvedOptions.Schedule?.Steps);
            var context = new RunContext(scheduler);

            using var scope = RunScope.Enter(scheduler);

            try
            {
                var callbackTask = test(context) ?? throw new InvalidOperationException("Braid run callback returned a null task.");
                await callbackTask.ConfigureAwait(false);
                await context.JoinAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RunException)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await scheduler.StopAsync().ConfigureAwait(false);
                throw scheduler.CreateException("braid run failed.", ex, RunFailureOrigin.UserTest);
            }
            finally
            {
                context.Complete();
            }
        }
    }
}
