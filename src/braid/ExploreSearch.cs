namespace Braid;

/// <summary>
/// Depth-first search over the schedules of a test. Each run replays a prefix of steps and then chooses the waiting worker itself; the search
/// keeps what every choice could have taken, and the next prefix changes the last choice that still has an untried step.
/// Every step comes from a real run, so a prefix fits the test also when the order of the workers changes which probes a worker hits.
/// </summary>
internal sealed class ExploreSearch
{
    private readonly List<Choice> _choices = [];
    private readonly int _maxHits;
    private ReplayStep[] _prefix = [];

    internal ExploreSearch(int maxHits)
    {
        _maxHits = maxHits;
    }

    /// <summary>Gets the steps the next run replays before it chooses itself. The first run replays none.</summary>
    internal IReadOnlyList<ReplayStep> Prefix => _prefix;

    /// <summary>Adds the choices that the last run made after its prefix and moves to the next prefix.</summary>
    /// <param name="choices">
    /// The steps the run could take at each choice after its prefix; it took the first of each. A run that stopped early reports the choices it reached.
    /// </param>
    /// <returns><see langword="true" /> if a prefix is left to run; <see langword="false" /> when every choice has been tried.</returns>
    internal bool MoveNext(IReadOnlyList<ReplayStep[]> choices)
    {
        // Only the choices up to the hit limit are explored. A run makes the later ones itself, and no prefix changes them.
        var hits = CountHits();
        for (var index = 0; index < choices.Count && hits < _maxHits; index++)
        {
            _choices.Add(new Choice(choices[index], 0));
            if (choices[index][0].Kind is ReplayStepKind.Hit)
                hits++;
        }

        while (_choices.Count > 0)
        {
            var last = _choices[^1];
            if (last.Taken + 1 < last.Steps.Length)
            {
                _choices[^1] = last with { Taken = last.Taken + 1 };
                _prefix = CreatePrefix();
                return true;
            }

            _choices.RemoveAt(_choices.Count - 1);
        }

        return false;
    }

    private int CountHits()
    {
        var hits = 0;
        for (var index = 0; index < _choices.Count; index++)
        {
            if (_choices[index].Step.Kind is ReplayStepKind.Hit)
                hits++;
        }

        return hits;
    }

    private ReplayStep[] CreatePrefix()
    {
        var prefix = new ReplayStep[_choices.Count];
        for (var index = 0; index < prefix.Length; index++)
            prefix[index] = _choices[index].Step;

        return prefix;
    }

    /// <summary>One choice on the current path: the steps that could be taken, and the one the path takes.</summary>
    /// <param name="Steps">The steps that could be taken, in the order they are tried.</param>
    /// <param name="Taken">The index of the step the path takes.</param>
    private readonly record struct Choice(ReplayStep[] Steps, int Taken)
    {
        internal ReplayStep Step => Steps[Taken];
    }
}
