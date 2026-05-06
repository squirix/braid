# braid roadmap

## v0.1 — Probe-based deterministic concurrency testing

The goal for v0.1 is for braid to provide a small .NET testing library that explores explicit async probe points and makes concurrency failures reproducible.

## Scope

- `Braid.RunAsync`
- `BraidContext.Fork`
- `BraidContext.JoinAsync`
- `BraidProbe.HitAsync`
- deterministic seed
- trace capture
- failure reporting through `BraidRunException`
- xUnit-compatible tests without a custom test runner

## Non-goals

- automatic `TaskScheduler` replacement
- binary rewriting
- actor runtime
- full Coyote replacement
- distributed-system testing
- linearizability checker

## Required Before v0.1

- stable public API names
- cancellation-token coverage in tests
- deterministic schedule replay
- trace formatting
- package metadata
- GitHub Actions CI
