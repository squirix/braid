namespace Braid;

/// <summary>
/// Represents a typed replay schedule for a braid run.
/// </summary>
public sealed class BraidSchedule
{
    private BraidSchedule(IReadOnlyList<BraidStep> steps)
    {
        Steps = steps;
    }

    /// <summary>
    /// Gets the replay steps in order.
    /// </summary>
    public IReadOnlyList<BraidStep> Steps { get; }

    /// <summary>
    /// Creates a replay schedule from the supplied steps. When the list is non-empty, the run must consume every step in order.
    /// </summary>
    /// <param name="steps">The worker release steps.</param>
    /// <returns>A replay schedule.</returns>
    public static BraidSchedule Replay(params BraidStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var copy = new BraidStep[steps.Length];
        for (var index = 0; index < steps.Length; index++)
        {
            copy[index] = steps[index];
            copy[index].Validate();
        }

        return new BraidSchedule(Array.AsReadOnly(copy));
    }

    internal void Validate()
    {
        foreach (var step in Steps)
        {
            step.Validate();
        }
    }
}
