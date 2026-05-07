namespace Braid.Internal;

internal sealed class BraidTask : IDisposable
{
    private readonly SemaphoreSlim permit = new(0, 1);

    public BraidTask(int id)
    {
        Id = id;
    }

    public Exception? Exception { get; set; }

    public int Id { get; }

    public string? LastProbeName { get; set; }

    public Task? RunningTask { get; set; }

    public BraidTaskState State { get; set; } = BraidTaskState.Waiting;

    public string WorkerId => $"worker-{Id}";

    public void Dispose() => permit.Dispose();

    public void Release() => permit.Release();

    public Task WaitForReleaseAsync(CancellationToken cancellationToken) => permit.WaitAsync(cancellationToken);
}
