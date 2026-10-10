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

    internal bool ProbeWaitInFlight { get; set; }

    /// <summary>Gets or sets the position of the worker's last release among the releases of the run, or zero while the worker has not started.</summary>
    internal int ReleaseOrder { get; set; }

    internal RunTaskState State { get; set; } = RunTaskState.Waiting;

    internal string WorkerId { get; }

    public void Dispose() => _permit.Dispose();

    /// <summary>
    /// Describes the probe wait that is still parked after the worker's operation returned. Such a wait belongs to a task that the worker
    /// started, or to a probe the worker hit itself, and that the worker did not wait for: nothing releases it as part of this worker any more.
    /// </summary>
    /// <returns>The failure message, or <see langword="null" /> when no probe wait of the worker is parked.</returns>
    internal string? DescribeProbeWaitLeftBehind()
    {
        return ProbeWaitInFlight && State is RunTaskState.Waiting or RunTaskState.Held
            ? $"Worker '{WorkerId}' returned while probe '{LastProbeName}' was still waiting on it. "
            + "A worker must wait for every probe that it, or a task it started, has hit before it returns."
            : null;
    }

    internal void Release() => _permit.Release();

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _permit.WaitAsync(cancellationToken);
}
