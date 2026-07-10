namespace Braid.Internal;

internal sealed class BraidTask : IDisposable
{
    private readonly SemaphoreSlim _permit = new(0, 1);

    public BraidTask(int id, string? workerId = null)
    {
        Id = id;
        WorkerId = workerId ?? $"worker-{id}";
    }

    public Exception? Exception { get; set; }

    public int Id { get; }

    public string? LastProbeName { get; set; }

    public bool ProbeWaitInFlight { get; set; }

    public BraidTaskState State { get; set; } = BraidTaskState.Waiting;

    public string WorkerId { get; }

    public void Release() => _permit.Release();

    public Task WaitForReleaseAsync(CancellationToken cancellationToken) => _permit.WaitAsync(cancellationToken);

    public void Dispose() => _permit.Dispose();
}
