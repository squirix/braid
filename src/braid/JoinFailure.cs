using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Braid;

/// <summary>
/// Remembers the failure of the first failed join of a run. A failed join is final: it stops the run and cancels the parked workers,
/// so a later join throws the same failure, not what the workers threw while they were stopping, and not a timeout that elapsed since.
/// </summary>
internal sealed class JoinFailure
{
    private Exception? _failure;

    /// <summary>Remembers the failure of a join unless an earlier one is remembered already.</summary>
    /// <param name="exception">The failure of the current join.</param>
    /// <returns>The remembered failure: the earlier one, or <paramref name="exception" />.</returns>
    internal Exception Record(Exception exception) => Interlocked.CompareExchange(ref _failure, exception, null) ?? exception;

    /// <summary>Remembers a new failure of a join unless an earlier one is remembered already, then throws the remembered failure.</summary>
    /// <param name="exception">The failure of the current join, not thrown yet.</param>
    [DoesNotReturn]
    internal void Throw(Exception exception)
    {
        var remembered = Record(exception);
        if (!ReferenceEquals(remembered, exception))
            ExceptionDispatchInfo.Throw(remembered);

        throw exception;
    }

    /// <summary>Throws the remembered failure, if any, with its original stack trace.</summary>
    internal void ThrowIfRecorded()
    {
        var failure = Volatile.Read(ref _failure);
        if (failure != null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
