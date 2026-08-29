namespace Braid.Internal;

internal static class ProbeCatalog
{
    private const string HitMarker = " hit ";

    internal static Dictionary<string, List<string>> ParseWorkerProbeSequences(IReadOnlyList<string> trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var sequences = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < trace.Count; index++)
        {
            var line = trace[index];
            var markerIndex = line.IndexOf(HitMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            var workerId = line[..markerIndex];
            var probeName = line[(markerIndex + HitMarker.Length)..];
            if (workerId.Length == 0 || probeName.Length == 0)
                continue;

            if (!sequences.TryGetValue(workerId, out var probes))
            {
                probes = [];
                sequences[workerId] = probes;
            }

            probes.Add(probeName);
        }

        return sequences;
    }
}
