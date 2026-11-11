using System;
using System.Collections.Generic;

namespace Braid;

/// <summary>Records run messages and scheduling decisions under the scheduler lock.</summary>
internal sealed class RunTrace
{
    /// <summary>Decisions made without consuming a step from the script.</summary>
    private readonly List<ReplayStep> _unscripted = [];

    internal List<string> Messages { get; } = [];

    internal void RecordRelease(RunTask task, bool consumedScriptStep)
    {
        var probe = task.LastProbeName;
        if (!consumedScriptStep)
            _unscripted.Add(probe == null ? ReplayStep.Start(task.WorkerId) : ReplayStep.Hit(task.WorkerId, probe));

        Messages.Add(probe == null ? $"{task.WorkerId} released" : $"{task.WorkerId} released at {probe}");
    }

    internal IReadOnlyList<ReplayStep> SnapshotExecutedSteps(IReadOnlyList<ReplayStep> script, int consumedSteps)
    {
        var count = _unscripted.Count + consumedSteps;
        if (count == 0)
            return [];

        // In a scripted run the only unscripted decisions are implicit starts, all before the script.
        var snapshot = new ReplayStep[count];
        _unscripted.CopyTo(snapshot);
        for (var index = 0; index < consumedSteps; index++)
            snapshot[_unscripted.Count + index] = script[index];

        return Array.AsReadOnly(snapshot);
    }
}
