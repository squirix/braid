#nullable disable

using System.Reflection.Emit;

namespace Braid.Tests;

/// <summary>Provides null values for tests that verify null-argument validation.</summary>
internal static class NullTestValues
{
    /// <summary>Gets a null string.</summary>
    internal static string String => null;

    /// <summary>Gets a null braid run callback.</summary>
    internal static Func<BraidContext, Task> RunCallback => null;

    /// <summary>Gets a null fork operation.</summary>
    internal static Func<Task> ForkOperation => null;

    /// <summary>Gets a null replay steps array.</summary>
    internal static BraidStep[] ReplaySteps => null;

    /// <summary>Gets a fork operation that returns null.</summary>
    internal static Func<Task> NullReturningFork { get; } = CreateNullReturningFork();

    /// <summary>Gets a run callback that returns null.</summary>
    internal static Func<BraidContext, Task> NullReturningRunCallback { get; } = CreateNullReturningRunCallback();

    private static Func<Task> CreateNullReturningFork()
    {
        var method = new DynamicMethod(
            "NullReturningFork",
            typeof(Task),
            Type.EmptyTypes,
            typeof(NullTestValues).Module,
            skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<Task>>();
    }

    private static Func<BraidContext, Task> CreateNullReturningRunCallback()
    {
        var method = new DynamicMethod(
            "NullReturningRunCallback",
            typeof(Task),
            [typeof(BraidContext)],
            typeof(NullTestValues).Module,
            skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<BraidContext, Task>>();
    }
}
