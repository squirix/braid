namespace Braid;

/// <summary>Defines seed, iteration, timeout, and replay options for a braid run.</summary>
public sealed class BraidOptions
{
    /// <summary>Gets the default options.</summary>
    public static BraidOptions Default { get; } = new();

    /// <summary>Gets or initializes the number of scheduling iterations to run.</summary>
    public int Iterations { get; init; } = 100;

    /// <summary>Gets or initializes an optional typed schedule used to replay a specific interleaving.</summary>
    public BraidSchedule? Schedule { get; init; }

    /// <summary>Gets or initializes the base seed. Each iteration adds its zero-based index to this seed.</summary>
    public int? Seed { get; init; }

    /// <summary>Gets or initializes the per-iteration timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        ValidatePositive(Iterations, nameof(Iterations), "Iterations must be positive.");
        ValidatePositive(Timeout, nameof(Timeout), "Timeout must be positive.");
        Schedule?.Validate();
    }

    private static void ValidatePositive(int value, string paramName, string message)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, message);
    }

    private static void ValidatePositive(TimeSpan value, string paramName, string message)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, message);
    }
}
