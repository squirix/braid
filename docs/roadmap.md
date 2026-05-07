# braid roadmap

## v0.1 - Probe-based deterministic concurrency testing

The v0.1 goal is a small .NET testing library that explores explicit async probe points and makes concurrency failures reproducible.

### Scope

- `Braid.RunAsync`
- `BraidContext.Fork`
- `BraidContext.JoinAsync`
- `BraidProbe.HitAsync`
- deterministic seed behavior
- typed replay schedules
- trace capture
- failure reporting through `BraidRunException`
- xUnit-compatible tests without a custom test runner

### v0.1 checklist

- public API polish
- deterministic seed behavior
- typed replay schedules
- failure reports
- schedule failure hardening
- README usage guide
- package metadata
- CI on Ubuntu
- stress smoke tests

### Non-goals

- automatic `TaskScheduler` replacement
- binary rewriting
- actor runtime
- full Coyote replacement
- distributed-system testing
- exhaustive model checking
- linearizability checker

## v0.2 ideas

These are future ideas, not v0.1 commitments.

- string schedule format
- xUnit output helper
- richer structured trace entries
- optional random exploration strategies
- probe source-generator/analyzer ideas
- integration examples with Squirix
