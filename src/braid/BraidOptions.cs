namespace Braid;

/// <summary>
/// Defines seed, iteration, timeout, and replay options for a braid run.
/// </summary>
public sealed class BraidOptions
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static BraidOptions Default { get; } = new();

    /// <summary>
    /// Gets or initializes the number of scheduling iterations to run.
    /// </summary>
    public int Iterations { get; init; } = 100;

    /// <summary>
    /// Gets or initializes an optional typed schedule used to replay a specific interleaving.
    /// </summary>
    public BraidSchedule? Schedule { get; init; }

    /// <summary>
    /// Gets or initializes the base seed. Each iteration adds its zero-based index to this seed.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Gets or initializes the per-iteration timeout.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

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

        _ = Schedule?.Steps;
    }
}
