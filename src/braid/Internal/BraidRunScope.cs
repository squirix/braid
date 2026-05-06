namespace Braid.Internal;

internal sealed class BraidRunScope : IDisposable
{
    private static readonly AsyncLocal<BraidScheduler?> SchedulerSlot = new();
    private static readonly AsyncLocal<BraidTask?> TaskSlot = new();

    private readonly BraidScheduler? previousScheduler;

    private BraidRunScope(BraidScheduler scheduler)
    {
        previousScheduler = SchedulerSlot.Value;
        SchedulerSlot.Value = scheduler;
    }

    internal static BraidScheduler? CurrentScheduler => SchedulerSlot.Value;

    internal static BraidTask? CurrentTask
    {
        get => TaskSlot.Value;
        set => TaskSlot.Value = value;
    }

    public void Dispose()
    {
        SchedulerSlot.Value = previousScheduler;
        TaskSlot.Value = null;
    }

    internal static BraidRunScope Enter(BraidScheduler scheduler) => new(scheduler);
}
