using Braid.Attributes;

namespace Braid;

/// <summary>Describes a worker and the probe it waits at, is held at, or was last released at, for diagnostic output.</summary>
/// <param name="WorkerId">The worker id.</param>
/// <param name="ProbeName">The probe name, or <see cref="NotStartedProbeName" /> or <see cref="StartProbeName" /> before the worker's first probe.</param>
[Immutable]
public readonly record struct ProbeWaitDiagnostic(string WorkerId, string ProbeName)
{
    /// <summary>The probe name reported for a waiting worker that has not started yet.</summary>
    public const string NotStartedProbeName = "(not started)";

    /// <summary>The probe name reported for a running worker that has not reached its first probe, and the placeholder probe name of a start step.</summary>
    public const string StartProbeName = "(start)";
}
