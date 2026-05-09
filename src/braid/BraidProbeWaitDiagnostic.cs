namespace Braid;

/// <summary>
/// Describes a worker waiting or held at a probe for diagnostic output.
/// </summary>
/// <param name="WorkerId">The worker id.</param>
/// <param name="ProbeName">The probe name.</param>
public readonly record struct BraidProbeWaitDiagnostic(string WorkerId, string ProbeName);
