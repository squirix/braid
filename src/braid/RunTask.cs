namespace Braid;

internal sealed class RunTask : IDisposable
{
    private readonly SemaphoreSlim _permit = new(0, 1);

    internal RunTask(int id, string? workerId = null)
    {
        Id = id;
        WorkerId = workerId ?? $"worker-{id}";
    }

    internal Exception? Exception { get; set; }

    internal int Id { get; }

    internal string? LastProbeName { get; set; }

    internal List<string> ProbeNames { get; } = [];

    internal bool ProbeWaitInFlight { get; set; }

    internal RunTaskState State { get; set; } = RunTaskState.Waiting;

    internal string WorkerId { get; }

    public void Dispose() => _permit.Dispose();

    internal void Release() => _permit.Release();

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _permit.WaitAsync(cancellationToken);
}
