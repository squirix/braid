using JetBrains.Annotations;

namespace Braid;

/// <summary>
/// Captures reproducibility state for a <see cref="RunException"/>.
/// </summary>
[PublicAPI]
public sealed record RunExceptionContext
{
    /// <summary>Initializes a new instance of the <see cref="RunExceptionContext"/> class.</summary>
    /// <param name="seed">The seed used for the failing iteration.</param>
    /// <param name="iteration">The failing iteration index.</param>
    /// <param name="traces">The recorded scheduling trace.</param>
    /// <param name="steps">The configured replay schedule.</param>
    /// <param name="schedulerDiagnostics">Scheduler state captured at failure time, when available.</param>
    public RunExceptionContext(int seed, int iteration, IReadOnlyList<string> traces, IReadOnlyList<ReplayStep> steps, SchedulerDiagnostics? schedulerDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(steps);

        Seed = seed;
        Iteration = iteration;
        Traces = Array.AsReadOnly([.. traces]);
        Steps = steps.Count > 0 ? Array.AsReadOnly([.. steps]) : Array.Empty<ReplayStep>();
        SchedulerDiagnostics = schedulerDiagnostics;
    }

    /// <summary>Gets the seed used for the failing iteration.</summary>
    public int Seed { get; }

    /// <summary>Gets the zero-based failing iteration index.</summary>
    public int Iteration { get; }

    /// <summary>Gets the recorded scheduling trace for the failing iteration.</summary>
    public IReadOnlyList<string> Traces { get; }

    /// <summary>Gets the configured replay schedule or an empty list when random scheduling was used.</summary>
    public IReadOnlyList<ReplayStep> Steps { get; }

    /// <summary>Gets scheduler diagnostics captured when the failure was recorded, when available.</summary>
    public SchedulerDiagnostics? SchedulerDiagnostics { get; }
}
