namespace Braid.Internal;

internal static class Explorer
{
    internal static async Task ExploreAsync(ExploreOptions options, Func<ExploreContext, Task> test, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(test);
        options.Validate();

        var callback = new ExploreCallback(test);
        var discoveryOptions = new RunOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Timeout = options.Timeout,
        };

        RunException? discoveryFailure = null;

        try
        {
            await Runner.RunAsync(callback.RunDiscoveryAsync, discoveryOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (RunException ex)
        {
            discoveryFailure = ex;
        }

        var workerProbeSequences = callback.DiscoveryContext?.WorkerProbeSequences ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (discoveryFailure != null && IsExplorationTargetFailure(discoveryFailure) && workerProbeSequences.Count == 0)
            throw discoveryFailure;

        if (workerProbeSequences.Count == 0)
            return;

        await ExploreGeneratedSchedulesAsync(options, callback, workerProbeSequences, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExploreGeneratedSchedulesAsync(
        ExploreOptions options,
        ExploreCallback callback,
        Dictionary<string, List<string>> workerProbeSequences,
        CancellationToken cancellationToken)
    {
        var readOnlySequences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in workerProbeSequences)
            readOnlySequences[entry.Key] = entry.Value.AsReadOnly();

        foreach (var steps in ScheduleEnumerator.EnumerateHitSchedules(readOnlySequences, options.MaxSchedules, options.MaxStepsPerSchedule))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var schedule = ReplaySchedule.Replay(steps);
            try
            {
                await RunScheduledExploreAttemptAsync(options, callback, schedule, cancellationToken).ConfigureAwait(false);
            }
            catch (RunException ex) when (IsExplorationTargetFailure(ex))
            {
                throw;
            }
            catch (RunException ex)
            {
                System.Diagnostics.Trace.TraceInformation($"Braid: skipping non-target schedule ({ex.Message}).");
            }
        }
    }

    private static bool IsExplorationTargetFailure(RunException exception)
    {
        if (exception.FailureOrigin != RunFailureOrigin.UserTest)
            return false;

        if (exception.InnerException == null)
            return false;

        return exception.InnerException is not RunException;
    }

    private static Task RunScheduledExploreAttemptAsync(ExploreOptions options, ExploreCallback callback, ReplaySchedule schedule, CancellationToken cancellationToken)
    {
        var runOptions = new RunOptions
        {
            Iterations = 1,
            Seed = options.Seed,
            Schedule = schedule,
            Timeout = options.Timeout,
        };

        return Runner.RunAsync(callback.RunReplayAsync, runOptions, cancellationToken);
    }

    private sealed class ExploreCallback(Func<ExploreContext, Task> test)
    {
        public RunContext? DiscoveryContext { get; private set; }

        public Task RunDiscoveryAsync(RunContext context)
        {
            DiscoveryContext = context;
            return test(new ExploreContext(context));
        }

        public Task RunReplayAsync(RunContext context) => test(new ExploreContext(context));
    }
}
