namespace Braid;

/// <summary>
/// Describes one scripted scheduler release at a named probe for a logical worker.
/// </summary>
public sealed class BraidScheduleStep
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BraidScheduleStep" /> class.
    /// </summary>
    /// <param name="workerId">The stable worker id, such as <c>worker-1</c>.</param>
    /// <param name="probeName">The probe name that must be waiting before the worker is released.</param>
    /// <exception cref="ArgumentException">Thrown when a required value is blank.</exception>
    public BraidScheduleStep(string workerId, string probeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeName);

        WorkerId = workerId;
        ProbeName = probeName;
    }

    /// <summary>
    /// Gets the stable worker id, such as <c>worker-1</c>.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Gets the probe name that must be waiting before the worker is released.
    /// </summary>
    public string ProbeName { get; }
}
