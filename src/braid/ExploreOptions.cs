using System.Runtime.InteropServices;

namespace Braid;

/// <summary>Bounds and seed options for bounded exploration.</summary>
/// <param name="Seed">The base seed used for discovery and replay runs.</param>
/// <param name="MaxSchedules">The maximum number of distinct replay schedules to try.</param>
/// <param name="MaxStepsPerSchedule">The maximum number of hit steps per generated replay schedule.</param>
/// <param name="Timeout">The per-run timeout.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExploreOptions(int Seed, int MaxSchedules, int MaxStepsPerSchedule, TimeSpan Timeout)
{
    internal readonly void Validate()
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
