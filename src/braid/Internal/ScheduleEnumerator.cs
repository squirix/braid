namespace Braid.Internal;

internal static class ScheduleEnumerator
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

    private readonly struct SearchFrame(int[] progress, List<ReplayStep> steps, int nextWorkerIndex)
    {
        internal int NextWorkerIndex { get; } = nextWorkerIndex;

        internal int[] Progress { get; } = progress;

        internal List<ReplayStep> Steps { get; } = steps;
    }
}
