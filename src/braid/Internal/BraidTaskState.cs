namespace Braid.Internal;

internal enum BraidTaskState
{
    /// <summary>
    /// The worker is blocked until the scheduler releases it.
    /// </summary>
    Waiting,

    /// <summary>
    /// The worker is currently executing user code.
    /// </summary>
    Running,

    /// <summary>
    /// The worker has finished executing.
    /// </summary>
    Completed,
}
