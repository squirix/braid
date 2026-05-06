namespace Braid;

/// <summary>
/// Describes one replay release at a named probe for a logical worker.
/// </summary>
/// <param name="WorkerId">The stable worker id, such as <c>worker-1</c>.</param>
/// <param name="ProbeName">The probe name that must be waiting before the worker is released.</param>
public readonly record struct BraidStep(string WorkerId, string ProbeName)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProbeName);
    }
}
