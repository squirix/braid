using Braid.Internal;

namespace Braid;

/// <summary>
/// Runs deterministic concurrency tests through an explicit probe-based scheduler.
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
            var scheduler = new BraidScheduler(seed, iteration, resolvedOptions.Timeout);
            var context = new BraidContext(scheduler);

            using var scope = BraidRunScope.Enter(scheduler);

            try
            {
                await test(context).ConfigureAwait(false);
                await context.JoinAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (BraidRunException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw scheduler.CreateException("Braid run failed.", ex);
            }
        }
    }
}
