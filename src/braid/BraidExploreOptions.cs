namespace Braid;

/// <summary>Bounds and seed options for bounded exploration.</summary>
public sealed class BraidExploreOptions
{
    internal BraidExploreOptions(int seed, int maxSchedules, int maxStepsPerSchedule, TimeSpan timeout)
    {
        Seed = seed;
        MaxSchedules = maxSchedules;
        MaxStepsPerSchedule = maxStepsPerSchedule;
        Timeout = timeout;
    }

    /// <summary>Gets the base seed used for discovery and replay runs.</summary>
    public int Seed { get; }

    /// <summary>Gets the maximum number of distinct replay schedules to try.</summary>
    public int MaxSchedules { get; }

    /// <summary>Gets the maximum number of hit steps per generated replay schedule.</summary>
    public int MaxStepsPerSchedule { get; }

    /// <summary>Gets the per-run timeout.</summary>
    public TimeSpan Timeout { get; }

    internal void Validate()
    {
        ValidatePositive(MaxSchedules, nameof(MaxSchedules), "MaxSchedules must be positive.");
        ValidatePositive(MaxStepsPerSchedule, nameof(MaxStepsPerSchedule), "MaxStepsPerSchedule must be positive.");
        ValidatePositive(Timeout, nameof(Timeout), "Timeout must be positive.");
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
