namespace Braid.Internal;

internal static class BraidProbeCatalog
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
            {
                continue;
            }

            var workerId = line[..markerIndex];
            var probeName = line[(markerIndex + HitMarker.Length)..];
            if (workerId.Length is 0 || probeName.Length is 0)
            {
                continue;
            }

            if (!sequences.TryGetValue(workerId, out var probes))
            {
                probes = [];
                sequences[workerId] = probes;
            }

            if (probes.Count is 0 || !string.Equals(probes[^1], probeName, StringComparison.Ordinal))
            {
                probes.Add(probeName);
            }
        }

        return sequences;
    }
}
