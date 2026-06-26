namespace Braid;

/// <summary>Defines replay step semantics at a named probe for a logical worker.</summary>
/// <param name="WorkerId">The stable worker id, such as <c>worker-1</c>.</param>
/// <param name="ProbeName">The probe name that must be waiting before the worker is released.</param>
/// <param name="Kind">The step kind.</param>
public readonly record struct BraidStep(string WorkerId, string ProbeName, BraidStepKind Kind = BraidStepKind.Hit)
{
    /// <summary>Creates a step that observes worker arrival at a probe and keeps it blocked.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>An arrival step.</returns>
    public static BraidStep Arrive(string workerId, string probeName) => new(workerId, probeName, BraidStepKind.Arrive);

    /// <summary>Creates a classic replay step that matches and releases a waiting worker at a probe.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>A hit step.</returns>
    public static BraidStep Hit(string workerId, string probeName) => new(workerId, probeName);

    /// <summary>Creates a step that releases a worker previously held at a probe.</summary>
    /// <param name="workerId">The stable worker id.</param>
    /// <param name="probeName">The probe name.</param>
    /// <returns>A release step.</returns>
    public static BraidStep Release(string workerId, string probeName) => new(workerId, probeName, BraidStepKind.Release);

    internal void Validate()
    {
        ValidateRequired(WorkerId, nameof(WorkerId));
        ValidateRequired(ProbeName, nameof(ProbeName));
        ValidateKind(Kind, nameof(Kind));
    }

    private static void ValidateKind(BraidStepKind kind, string paramName)
    {
        switch (kind)
        {
            case BraidStepKind.Hit:
            case BraidStepKind.Arrive:
            case BraidStepKind.Release:
                return;
            default:
                throw new ArgumentOutOfRangeException(paramName, kind, "Unknown braid step kind.");
        }
    }

    private static void ValidateRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }
}
