namespace Braid;

internal sealed class SchedulerJoinContext
{
    internal required List<RunTask> Tasks { get; init; }

    internal required int NextScheduleStep { get; set; }

    internal required ReplayScript? Script { get; init; }

    internal required bool StartsInForkOrder { get; init; }

    internal IReadOnlyList<ReplayStep>? Steps => Script?.Steps;

    internal required DeterministicRandom Random { get; init; }

    internal required List<string> Trace { get; init; }

    internal required Func<string, Exception?, RunFailureOrigin, RunException> CreateException { get; init; }
}
