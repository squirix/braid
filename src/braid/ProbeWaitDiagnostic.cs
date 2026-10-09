using Braid.Attributes;

namespace Braid;

/// <summary>Describes a worker and the probe it waits at, is held at, or was last released at, for diagnostic output.</summary>
/// <param name="WorkerId">The worker id.</param>
/// <param name="ProbeName">The probe name.</param>
[Immutable]
public readonly record struct ProbeWaitDiagnostic(string WorkerId, string ProbeName);
