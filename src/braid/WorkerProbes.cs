namespace Braid;

/// <summary>The probes one worker hit during a run, in order. A worker that hit no probe has an empty list.</summary>
/// <param name="WorkerId">The worker id.</param>
/// <param name="ProbeNames">The names of the probes the worker hit.</param>
internal readonly record struct WorkerProbes(string WorkerId, IReadOnlyList<string> ProbeNames);
