using Braid.Internal;

namespace Braid;

/// <summary>Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.</summary>
public static class BraidRunner
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
    /// <exception cref="BraidRunException">A test failure was found under a replay schedule or during discovery.</exception>
    public static Task ExploreAsync(Action<BraidExploreOptionsBuilder> configure, Func<BraidExploreContext, Task> test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(test);

        var builder = new BraidExploreOptionsBuilder();
        configure(builder);
        return ExploreAsync(builder.Build(), test, cancellationToken);
    }

    /// <inheritdoc cref="ExploreAsync(Action{BraidExploreOptionsBuilder}, Func{BraidExploreContext, Task}, CancellationToken)" />
    public static Task ExploreAsync(BraidExploreOptions options, Func<BraidExploreContext, Task> test, CancellationToken cancellationToken) =>
        BraidExplorer.ExploreAsync(options, test, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="BraidContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="test" /> is null.</exception>
    /// <exception cref="InvalidOperationException">A braid run is already active, or the callback returned a null task.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was canceled.</exception>
    /// <exception cref="BraidRunException">A forked worker failed, the run timed out, or scheduling could not satisfy the replay script.</exception>
    public static Task RunAsync(Func<BraidContext, Task> test, CancellationToken cancellationToken) => RunAsync(test, null, cancellationToken);

    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="BraidContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
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
    /// <exception cref="BraidRunException">A forked worker failed, the run timed out, or scheduling could not satisfy the replay script.</exception>
    public static Task RunAsync(Func<BraidContext, Task> test, BraidOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (BraidRunScope.CurrentScheduler is not null)
            throw new InvalidOperationException("Nested braid runs are not supported.");

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedOptions = options ?? BraidOptions.Default;
        resolvedOptions.Validate();

        return RunAsyncCoreAsync(test, resolvedOptions, cancellationToken);
    }

    private static async Task RunAsyncCoreAsync(Func<BraidContext, Task> test, BraidOptions resolvedOptions, CancellationToken cancellationToken)
    {
        var baseSeed = resolvedOptions.Seed ?? Environment.TickCount;

        for (var iteration = 0; iteration < resolvedOptions.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seed = unchecked(baseSeed + iteration);
            using var scheduler = new BraidScheduler(seed, iteration, resolvedOptions.Timeout, resolvedOptions.Schedule?.Steps);
            var context = new BraidContext(scheduler);

            using var scope = BraidRunScope.Enter(scheduler);

            try
            {
                var callbackTask = test(context) ?? throw new InvalidOperationException("Braid run callback returned a null task.");
                await callbackTask.ConfigureAwait(false);
                await context.JoinAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (BraidRunException)
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
                throw scheduler.CreateException("braid run failed.", ex, BraidRunFailureOrigin.UserTest);
            }
            finally
            {
                context.Complete();
            }
        }
    }
}
