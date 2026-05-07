using Braid.Internal;

namespace Braid;

/// <summary>
/// Runs deterministic concurrency tests by controlling logical workers at explicit async probe points.
/// </summary>
public static class Braid
{
    /// <summary>
    /// Runs the supplied test callback across one or more deterministic scheduling iterations.
    /// </summary>
    /// <param name="test">The test callback to execute.</param>
    /// <param name="options">The run options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task" /> that completes when all iterations pass.</returns>
    public static async Task RunAsync(Func<BraidContext, Task> test, BraidOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);

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
                await test(context).ConfigureAwait(false);
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
        }
    }
}
