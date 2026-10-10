namespace Braid.Tests;

/// <summary>Keeps the README C# snippets compiling: each snippet lives in this file and must match the README verbatim.</summary>
public sealed class ReadmeSnippetTests : TestBase
{
    private const string SnippetFile = "tests/braid.tests/ReadmeSnippetTests.cs";

    /// <summary>Verifies every C# block in the README appears in this file, ignoring indentation and using directives.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReadmeSnippetsMatchCompiledCode(CancellationToken cancellationToken)
    {
        var root = FindRepositoryRoot();
        var readme = await File.ReadAllLinesAsync(Path.Combine(root, "README.md"), cancellationToken);
        var source = NormalizeLines(await File.ReadAllLinesAsync(Path.Combine(root, SnippetFile), cancellationToken));

        var blocks = ExtractCSharpBlocks(readme);
        _ = await Assert.That(blocks.Count).IsGreaterThan(0);

        foreach (var block in blocks)
        {
            var found = ContainsSequence(source, NormalizeLines(block));
            _ = await Assert.That(found).IsTrue().Because($"a README snippet starting with \"{block[0]}\" is missing from {SnippetFile}");
        }
    }

    /// <summary>Runs the README quick start.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task QuickStartRuns(CancellationToken cancellationToken)
    {
        // Inside a test method: cancellationToken is the test framework's token (or CancellationToken.None).
        var workerCompleted = false;
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(ReplayStep.Hit("worker-1", "ready")),
        };

        await Runner.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await Probe.HitAsync("ready", cancellationToken);
                    workerCompleted = true;
                });

                await context.JoinAsync(cancellationToken);
            },
            options,
            cancellationToken);

        // workerCompleted is true here.
        _ = await Assert.That(workerCompleted).IsTrue();
    }

    /// <summary>Runs the README exploration snippet.</summary>
    /// <param name="cancellationToken">The cancellation token for the current test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ExplorationRuns(CancellationToken cancellationToken)
    {
        const int expected = 1;
        var observed = 0;

        async Task ReaderAsync()
        {
            await Probe.HitAsync("read", cancellationToken);
        }

        async Task WriterAsync()
        {
            await Probe.HitAsync("write", cancellationToken);
            observed = expected;
        }

        await Runner.ExploreAsync(
            static options => options
                .WithMaxSchedules(1_000)
                .WithMaxStepsPerSchedule(100),
            async braid =>
            {
                await braid.WorkerAsync("reader", ReaderAsync);
                await braid.WorkerAsync("writer", WriterAsync);

                await braid.JoinAsync(cancellationToken);
                if (observed != expected)
                    throw new InvalidOperationException($"Observed {observed}, expected {expected}.");
            },
            cancellationToken);

        _ = await Assert.That(observed).IsEqualTo(expected);
    }

    /// <summary>Builds the README replay schedule.</summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Test]
    public async Task ReplayScheduleBuilds()
    {
        var options = new RunOptions
        {
            Iterations = 1,
            Schedule = ReplaySchedule.Replay(
                ReplayStep.Hit("worker-1", "before-read"),
                ReplayStep.Hit("worker-2", "before-read"),
                ReplayStep.Hit("worker-1", "before-write"),
                ReplayStep.Hit("worker-2", "before-write")),
        };

        _ = await Assert.That(options.Schedule.Steps.Count).IsEqualTo(4);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "braid.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root with braid.slnx was not found.");
    }

    private static List<string[]> ExtractCSharpBlocks(string[] lines)
    {
        var blocks = new List<string[]>();
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (current == null)
            {
                if (string.Equals(line.Trim(), "```csharp", StringComparison.Ordinal))
                    current = [];

                continue;
            }

            if (string.Equals(line.Trim(), "```", StringComparison.Ordinal))
            {
                blocks.Add([.. current]);
                current = null;
                continue;
            }

            current.Add(line);
        }

        return blocks;
    }

    private static List<string> NormalizeLines(IEnumerable<string> lines)
    {
        var normalized = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("using ", StringComparison.Ordinal))
                normalized.Add(trimmed);
        }

        return normalized;
    }

    private static bool ContainsSequence(List<string> source, List<string> sequence)
    {
        for (var start = 0; start + sequence.Count <= source.Count; start++)
        {
            var matches = true;
            for (var index = 0; index < sequence.Count && matches; index++)
                matches = string.Equals(source[start + index], sequence[index], StringComparison.Ordinal);

            if (matches)
                return true;
        }

        return false;
    }
}
