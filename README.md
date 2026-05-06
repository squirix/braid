# Braid

Braid is an experimental deterministic concurrency testing library for .NET.

The first prototype is explicit probe-based: production code can expose named scheduling points, while tests use Braid to explore reproducible interleavings.

```csharp
await Braid.RunAsync(async ctx =>
{
    ctx.Fork(async () =>
    {
        await BraidProbe.HitAsync("before-write");
    });

    await ctx.JoinAsync();
});
```

## Goals

- Make async race conditions reproducible.
- Keep integration test-framework-first.
- Start with explicit probes before attempting deep TaskScheduler integration.
- Print seed and trace on failure.

## Package shape

- `Braid`
- `Braid.Xunit` later
- `Braid.NUnit` later
- `Braid.MSTest` later
