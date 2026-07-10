namespace Braid.Internal;

internal static class BraidExplorer
{
    internal static async Task ExploreAsync(
        BraidExploreOptions options,
        Func<BraidExploreContext, Task> test,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(test);
        options.Validate();

        var callback = new ExploreCallback(test);
        var discoveryOptions = new BraidOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Timeout = options.Timeout,
        };

        IReadOnlyList<string> discoveryTrace = [];
        BraidRunException? discoveryFailure = null;

        try
        {
            await BraidRunner.RunAsync(callback.RunDiscoveryAsync, discoveryOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (BraidRunException ex)
        {
            discoveryFailure = ex;
            discoveryTrace = ex.Trace;
        }

        if (discoveryTrace.Count is 0 && callback.DiscoveryContext is not null)
        {
            discoveryTrace = callback.DiscoveryContext.TraceSteps;
        }

        var workerProbeSequences = BraidProbeCatalog.ParseWorkerProbeSequences(discoveryTrace);
        if (discoveryFailure is not null && IsExplorationTargetFailure(discoveryFailure) && workerProbeSequences.Count is 0)
        {
            throw discoveryFailure;
        }

        if (workerProbeSequences.Count is 0)
        {
            return;
        }

        await ExploreGeneratedSchedulesAsync(options, callback, workerProbeSequences, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExploreGeneratedSchedulesAsync(
        BraidExploreOptions options,
        ExploreCallback callback,
        Dictionary<string, List<string>> workerProbeSequences,
        CancellationToken cancellationToken)
    {
        var readOnlySequences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in workerProbeSequences)
        {
            readOnlySequences[entry.Key] = entry.Value.AsReadOnly();
        }

        foreach (var steps in BraidScheduleEnumerator.EnumerateHitSchedules(readOnlySequences, options.MaxSchedules, options.MaxStepsPerSchedule))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var schedule = BraidSchedule.Replay(steps);
            try
            {
                await RunScheduledExploreAttemptAsync(options, callback, schedule, cancellationToken).ConfigureAwait(false);
            }
            catch (BraidRunException ex) when (IsExplorationTargetFailure(ex))
            {
                throw;
            }
            catch (BraidRunException)
            {
                // Invalid or non-failing schedules are skipped during bounded search.
            }
        }
    }

    private static Task RunScheduledExploreAttemptAsync(
        BraidExploreOptions options,
        ExploreCallback callback,
        BraidSchedule schedule,
        CancellationToken cancellationToken)
    {
        var runOptions = new BraidOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Schedule = schedule,
            Timeout = options.Timeout,
        };

        return BraidRunner.RunAsync(callback.RunReplayAsync, runOptions, cancellationToken);
    }

    private static bool IsExplorationTargetFailure(BraidRunException exception)
    {
        if (exception.FailureOrigin is not BraidRunFailureOrigin.UserTest)
        {
            return false;
        }

        if (exception.InnerException is null)
        {
            return false;
        }

        return exception.InnerException is not BraidRunException;
    }

    private sealed class ExploreCallback(Func<BraidExploreContext, Task> test)
    {
        public BraidContext? DiscoveryContext { get; private set; }

        public Task RunDiscoveryAsync(BraidContext context)
        {
            DiscoveryContext = context;
            return test(new BraidExploreContext(context));
        }

        public Task RunReplayAsync(BraidContext context) => test(new BraidExploreContext(context));
    }
}
