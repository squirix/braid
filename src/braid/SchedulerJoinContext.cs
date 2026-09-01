namespace Braid;

internal struct SchedulerJoinContext
{
    internal required List<RunTask> Tasks { get; init; }

    internal required int NextScheduleStep { get; set; }

    internal required IReadOnlyList<ReplayStep>? Steps { get; init; }

    internal required DeterministicRandom Random { get; init; }

    internal required List<string> Trace { get; init; }

    internal required Func<string, Exception?, RunFailureOrigin, RunException> CreateException { get; init; }
}
