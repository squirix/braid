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
    /// <typeparam name="TException">The type of the failure.</typeparam>
    /// <param name="exception">The failure of the current join.</param>
    /// <returns><paramref name="exception" />.</returns>
    internal TException Record<TException>(TException exception)
        where TException : Exception
    {
        _ = Interlocked.CompareExchange(ref _failure, exception, null);
        return exception;
    }

    /// <summary>Throws the remembered failure, if any, with its original stack trace.</summary>
    internal void ThrowIfRecorded()
    {
        var failure = Volatile.Read(ref _failure);
        if (failure != null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
