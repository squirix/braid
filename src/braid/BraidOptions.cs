namespace Braid;

/// <summary>
/// Defines options for a braid run.
/// </summary>
public sealed class BraidOptions
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static BraidOptions Default { get; } = new();

    /// <summary>
    /// Gets or initializes the number of scheduling iterations.
    /// </summary>
    public int Iterations { get; init; } = 100;

    /// <summary>
    /// Gets or initializes the base seed. When unset, a process-local seed is used.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Gets or initializes the per-iteration timeout.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or initializes an optional scripted schedule used to replay a specific interleaving.
    /// </summary>
    public IReadOnlyList<BraidScheduleStep>? Schedule { get; init; }

    internal void Validate()
    {
        if (Iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations), Iterations, "Iterations must be positive.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Timeout must be positive.");
        }

        if (Schedule is null)
        {
            return;
        }

        foreach (var step in Schedule)
        {
            ArgumentNullException.ThrowIfNull(step);
        }
    }
}
