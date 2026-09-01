namespace Braid.Internal;

internal static class Explorer
{
    internal static async Task ExploreAsync(ExploreOptions options, Func<ExploreContext, Task> test, CancellationToken cancellationToken)
    {
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

    private static class ScheduleEnumerator
    {
        internal static IEnumerable<IReadOnlyList<ReplayStep>> EnumerateHitSchedules(
            IReadOnlyDictionary<string, IReadOnlyList<string>> workerProbeSequences,
            int maxSchedules,
            int maxStepsPerSchedule)
        {
            ArgumentNullException.ThrowIfNull(workerProbeSequences);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSchedules);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerSchedule);

            return EnumerateHitSchedulesCore(workerProbeSequences, maxSchedules, maxStepsPerSchedule);
        }

        private static bool AllWorkersCompleted(string[] workerIds, IReadOnlyList<string>[] sequences, int[] progress)
        {
            for (var index = 0; index < workerIds.Length; index++)
            {
                if (progress[index] < sequences[index].Count)
                    return false;
            }

            return true;
        }

        private static int[] CloneProgress(int[] progress)
        {
            var copy = new int[progress.Length];
            for (var index = 0; index < progress.Length; index++)
                copy[index] = progress[index];

            return copy;
        }

        private static ReplayStep[] CopySteps(List<ReplayStep> steps)
        {
            var copy = new ReplayStep[steps.Count];
            for (var index = 0; index < steps.Count; index++)
                copy[index] = steps[index];

            return copy;
        }

        private static IEnumerable<IReadOnlyList<ReplayStep>> EnumerateHitSchedulesCore(IReadOnlyDictionary<string, IReadOnlyList<string>> sequences, int maxSchedules, int maxSteps)
        {
            if (!TryCreateWorkerState(sequences, out var workerIds, out var lists))
                yield break;

            var yielded = 0;
            var stack = new Stack<SearchFrame>();
            stack.Push(new SearchFrame(new int[workerIds.Length], [], 0));

            while (stack.Count > 0)
            {
                if (yielded >= maxSchedules)
                    break;

                var frame = stack.Pop();
                if (frame.Steps.Count == maxSteps)
                {
                    if (frame.Steps.Count > 0 && yielded < maxSchedules)
                    {
                        yielded++;
                        yield return CopySteps(frame.Steps);
                    }

                    continue;
                }

                if (AllWorkersCompleted(workerIds, lists, frame.Progress))
                {
                    if (frame.Steps.Count > 0 && frame.Steps.Count <= maxSteps && yielded < maxSchedules)
                    {
                        yielded++;
                        yield return CopySteps(frame.Steps);
                    }

                    continue;
                }

                ScheduleNextWorker(workerIds, lists, frame, stack);
            }
        }

        private static void ScheduleNextWorker(string[] workerIds, IReadOnlyList<string>[] sequences, SearchFrame frame, Stack<SearchFrame> stack)
        {
            for (var workerIndex = frame.NextWorkerIndex; workerIndex < workerIds.Length; workerIndex++)
            {
                if (frame.Progress[workerIndex] >= sequences[workerIndex].Count)
                    continue;

                var nextProgress = CloneProgress(frame.Progress);
                nextProgress[workerIndex]++;
                var nextSteps = new List<ReplayStep>(frame.Steps)
                {
                    ReplayStep.Hit(workerIds[workerIndex], sequences[workerIndex][frame.Progress[workerIndex]]),
                };

                stack.Push(new SearchFrame(frame.Progress, frame.Steps, workerIndex + 1));
                stack.Push(new SearchFrame(nextProgress, nextSteps, 0));
                return;
            }
        }

        private static void SortWorkerEntries(string[] workerIds, IReadOnlyList<string>[] sequences)
        {
            for (var i = 1; i < workerIds.Length; i++)
            {
                var workerId = workerIds[i];
                var sequence = sequences[i];
                var j = i;
                while (j > 0 && string.CompareOrdinal(workerIds[j - 1], workerId) > 0)
                {
                    workerIds[j] = workerIds[j - 1];
                    sequences[j] = sequences[j - 1];
                    j--;
                }

                workerIds[j] = workerId;
                sequences[j] = sequence;
            }
        }

        private static bool TryCreateWorkerState(IReadOnlyDictionary<string, IReadOnlyList<string>> workerProbeSequences, out string[] workerIds, out IReadOnlyList<string>[] sequences)
        {
            workerIds = [];
            sequences = [];

            if (workerProbeSequences.Count == 0)
                return false;

            workerIds = new string[workerProbeSequences.Count];
            sequences = new IReadOnlyList<string>[workerProbeSequences.Count];
            var index = 0;
            foreach (var pair in workerProbeSequences)
            {
                if (pair.Value.Count == 0)
                {
                    workerIds = [];
                    sequences = [];
                    return false;
                }

                workerIds[index] = pair.Key;
                sequences[index] = pair.Value;
                index++;
            }

            SortWorkerEntries(workerIds, sequences);
            return true;
        }

        private readonly record struct SearchFrame(int[] Progress, List<ReplayStep> Steps, int NextWorkerIndex);
    }

    private sealed class ExploreCallback
    {
        private readonly Func<ExploreContext, Task> _callback;

        internal ExploreCallback(Func<ExploreContext, Task> callback)
        {
            _callback = callback;
        }

        internal RunContext? DiscoveryContext { get; private set; }

        internal Task RunDiscoveryAsync(RunContext context)
        {
            DiscoveryContext = context;
            return _callback(new ExploreContext(context));
        }

        internal Task RunReplayAsync(RunContext context) => _callback(new ExploreContext(context));
    }
}
