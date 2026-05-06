namespace Braid.Internal;

internal sealed class BraidTask
{
    private readonly SemaphoreSlim permit = new(0, 1);

    internal BraidTask(int id)
    {
        Id = id;
    }

    internal Exception? Exception { get; set; }

    internal int Id { get; }

    internal string? LastProbeName { get; set; }

    internal Task? RunningTask { get; set; }

    internal BraidTaskState State { get; set; } = BraidTaskState.Waiting;

    internal string WorkerId => $"worker-{Id}";

    internal void Release() => permit.Release();

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => permit.WaitAsync(cancellationToken);
}
