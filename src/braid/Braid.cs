using Braid.Internal;

namespace Braid;

/// <summary>
/// Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.
/// </summary>
public static class Braid
{
    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// After the callback task completes successfully, forked workers are joined automatically; an explicit
    /// <see cref="BraidContext.JoinAsync(System.Threading.CancellationToken)" /> at the end of the callback is optional.
    /// The callback must not return null.
    /// </summary>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    public static Task RunAsync(Func<BraidContext, Task> test, CancellationToken cancellationToken)
        => RunAsync(test, null, cancellationToken);

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
    public static async Task RunAsync(Func<BraidContext, Task> test, BraidOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        if (BraidRunScope.CurrentScheduler is not null)
        {
            throw new InvalidOperationException("Nested braid runs are not supported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedOptions = options ?? BraidOptions.Default;
        resolvedOptions.Validate();

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
                throw scheduler.CreateException("braid run failed.", ex);
            }
            finally
            {
                context.Complete();
            }
        }
    }
}
