using System.Runtime.InteropServices;
using Braid.Attributes;

namespace Braid;

/// <summary>Bounds for bounded exploration.</summary>
/// <param name="MaxSchedules">The maximum number of schedules to run.</param>
/// <param name="MaxStepsPerSchedule">The number of hit steps of a schedule whose order is explored; start steps do not count. After them a run releases the worker that has waited longest, so the test can run to completion.</param>
/// <param name="Timeout">The per-run timeout.</param>
[Immutable]
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExploreOptions(int MaxSchedules, int MaxStepsPerSchedule, TimeSpan Timeout)
{
    internal void Validate()
    {
        ValidatePositive(MaxSchedules, nameof(MaxSchedules), "MaxSchedules must be positive.");
        ValidatePositive(MaxStepsPerSchedule, nameof(MaxStepsPerSchedule), "MaxStepsPerSchedule must be positive.");
        RunOptions.ValidateTimeout(Timeout, nameof(Timeout));
    }

    private static void ValidatePositive(int value, string paramName, string message)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, message);
    }
}
