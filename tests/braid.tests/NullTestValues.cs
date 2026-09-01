#nullable disable

namespace Braid.Tests;

/// <summary>Provides null values for tests that verify null-argument validation.</summary>
internal static class NullTestValues
{
    private static readonly Task[] NullTaskHolder = new Task[1];

    /// <summary>Gets a null fork operation.</summary>
    internal static Func<Task> ForkOperation => null;

    /// <summary>Gets a fork operation that returns null.</summary>
    internal static Func<Task> NullReturningFork => static () => NullTaskHolder[0];

    /// <summary>Gets a run callback that returns null.</summary>
    internal static Func<RunContext, Task> NullReturningRunCallback => static _ => NullTaskHolder[0];

    /// <summary>Gets a null replay steps array.</summary>
    internal static ReplayStep[] ReplaySteps => null;

    /// <summary>Gets a null braid run callback.</summary>
    internal static Func<RunContext, Task> RunCallback => null;

    /// <summary>Gets a null string.</summary>
    internal static string String => null;
}
