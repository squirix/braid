using Braid.Attributes;

namespace Braid;

[Immutable]
internal sealed class RunScope : IDisposable
{
    private static readonly AsyncLocal<Scheduler?> SchedulerSlot = new();
    private static readonly AsyncLocal<RunTask?> TaskSlot = new();

    private readonly Scheduler? _previousScheduler;

    private RunScope(Scheduler scheduler)
    {
        _previousScheduler = SchedulerSlot.Value;
        SchedulerSlot.Value = scheduler;
    }

    internal static Scheduler? CurrentScheduler => SchedulerSlot.Value;

    internal static RunTask? CurrentTask
    {
        get => TaskSlot.Value;
        set => TaskSlot.Value = value;
    }

    public void Dispose()
    {
        SchedulerSlot.Value = _previousScheduler;
        TaskSlot.Value = null;
    }

    internal static RunScope Enter(Scheduler scheduler) => new(scheduler);
}
