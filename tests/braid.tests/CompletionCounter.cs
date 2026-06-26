namespace Braid.Tests;

/// <summary>Holds a mutable count shared across forked workers in tests.</summary>
public sealed class CompletionCounter
{
    private int _value;

    /// <summary>Gets the current count.</summary>
    public int Value => Volatile.Read(ref _value);

    /// <summary>Atomically increments the count by one.</summary>
    /// <returns>The new count.</returns>
    public int Increment() => Interlocked.Increment(ref _value);
}
