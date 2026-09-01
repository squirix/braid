namespace Braid.Internal;

internal sealed class RunTask : IDisposable
{
    private readonly SemaphoreSlim _permit = new(0, 1);

    public RunTask(int id, string? workerId = null)
    {
        Id = id;
        WorkerId = workerId ?? $"worker-{id}";
    }

    public Exception? Exception { get; set; }

    public int Id { get; }

    public string? LastProbeName { get; set; }

    public bool ProbeWaitInFlight { get; set; }

    public RunTaskState State { get; set; } = RunTaskState.Waiting;

    public string WorkerId { get; }

    internal List<string> ProbeNames { get; } = [];

    public void Release() => _permit.Release();

    public Task WaitForReleaseAsync(CancellationToken cancellationToken) => _permit.WaitAsync(cancellationToken);

    public void Dispose() => _permit.Dispose();
}
