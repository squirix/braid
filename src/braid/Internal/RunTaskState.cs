namespace Braid.Internal;

internal enum RunTaskState
{
    /// <summary>The worker is blocked until the scheduler releases it.</summary>
    Waiting = 0,

    /// <summary>The worker is blocked at a probe and explicitly held by a scripted arrival step.</summary>
    Held = 1,

    /// <summary>The worker is currently executing user code.</summary>
    Running = 2,

    /// <summary>The worker has finished executing.</summary>
    Completed = 3,
}
