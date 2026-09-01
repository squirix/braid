using Braid.Attributes;

namespace Braid;

/// <summary>Defines replay step semantics at a named probe for a logical worker.</summary>
/// <param name="WorkerId">The stable worker id, such as worker-1.</param>
/// <param name="ProbeName">The probe name that must be waiting before the worker is released.</param>
/// <param name="Kind">The step kind.</param>
[Immutable]
public readonly record struct ReplayStep(string WorkerId, string ProbeName, ReplayStepKind Kind = ReplayStepKind.Hit)
{
    /// <summary>Creates a step that observes worker arrival at a probe and keeps it blocked.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>An arrival step.</returns>
    public static ReplayStep Arrive(string workerId, string probeName) => new(workerId, probeName, ReplayStepKind.Arrive);

    /// <summary>Creates a classic replay step that matches and releases a waiting worker at a probe.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>A hit step.</returns>
    public static ReplayStep Hit(string workerId, string probeName) => new(workerId, probeName);

    /// <summary>Creates a step that releases a worker previously held at a probe.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>A release step.</returns>
    public static ReplayStep Release(string workerId, string probeName) => new(workerId, probeName, ReplayStepKind.Release);

    internal void Validate()
    {
        ValidateRequired(WorkerId, nameof(WorkerId));
        ValidateRequired(ProbeName, nameof(ProbeName));
        ValidateKind(Kind, nameof(Kind));
    }

    private static void ValidateKind(ReplayStepKind kind, string paramName)
    {
        switch (kind)
        {
            case ReplayStepKind.Hit:
            case ReplayStepKind.Arrive:
            case ReplayStepKind.Release:
                return;
            default:
                throw new ArgumentOutOfRangeException(paramName, kind, "Unknown braid step kind.");
        }
    }

    private static void ValidateRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
    }
}
