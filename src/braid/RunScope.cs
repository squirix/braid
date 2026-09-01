using Braid.Attributes;

namespace Braid;

[Immutable]
internal sealed class RunScope : IDisposable
{
    private static readonly AsyncLocal<Scheduler?> SchedulerSlot = new();

    private readonly Scheduler? _previousScheduler;

    private RunScope(Scheduler scheduler)
    {
        _previousScheduler = SchedulerSlot.Value;
        SchedulerSlot.Value = scheduler;
    }

    internal static Scheduler? CurrentScheduler => SchedulerSlot.Value;

    public void Dispose()
    {
        SchedulerSlot.Value = _previousScheduler;
        RunTaskSlot.Clear();
    }

    internal static RunScope Enter(Scheduler scheduler) => new(scheduler);
}
