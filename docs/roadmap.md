# braid roadmap

braid is a deterministic concurrency testing library for .NET (currently **.NET 10**) using explicit async probe points.

The project intentionally focuses on small, reproducible async interleavings. It does not try to replace Coyote, intercept every `await`, rewrite binaries, become a distributed-system test framework, or provide exhaustive model checking.

**Recommended next release:** **v0.4.0** — test-framework integration and diagnostics polish (see below).

This roadmap matches [CHANGELOG.md](../CHANGELOG.md) through **v0.3.1** for completed work.

## Release policy

- Patch releases are for documentation, diagnostics hardening, scheduler bug fixes, packaging fixes, and examples.
- Minor releases are for user-visible testing capabilities.
- Runtime semantics must remain deterministic and easy to explain.
- New features should preserve the explicit-probe model.

## Completed releases

### v0.1.0 — Probe-based deterministic concurrency testing

Goal: provide a small .NET library for reproducible async interleavings through explicit probe points.

Delivered (per [CHANGELOG.md](../CHANGELOG.md) **0.1.0**):

- deterministic explicit-probe concurrency testing with `Braid.RunAsync`
- fork/join orchestration through `BraidContext`
- probe control with `BraidProbe.HitAsync`
- typed replay schedules through `BraidSchedule` and `BraidStep`
- failure reports with seed, iteration, schedule, and trace

### v0.2.0 — Arrive / release replay control

Goal: support interleaving assertions where a worker can be observed at a probe and intentionally held.

Delivered (per [CHANGELOG.md](../CHANGELOG.md) **0.2.0**):

- typed arrive/hold/release replay steps for true interleaving assertions (`BraidStep.Arrive`, `BraidStep.Release`, alongside existing `Hit` replay)

### v0.2.1 — Replay scheduler hardening

Goal: tighten replay behavior and clean up package/docs consistency after v0.2.0.

Delivered:

- consistent package version references across README and release documentation
- arrive/hold/release scheduler hardening
- diagnostics for invalid or incomplete replay schedules
- explicit cancellation-token usage on public async entry points and examples
- aligned private field naming across core scheduler code and examples

### v0.3.0 — Text replay schedules and failure diagnostics

Goal: make replay schedules copyable, parseable, and easier to move from failure reports into regression tests.

Delivered:

- `BraidSchedule.Parse(...)` / `BraidSchedule.TryParse(...)`
- `BraidSchedule.ToReplayText()` and canonical replay text format
- replay text in failure reports when a typed replay schedule was configured and can be exported
- scheduler-state diagnostics in failure reports when available (for example last matched replay step, waiting workers, held workers, unused replay steps)
- `examples/cache-cas-race` with walkthrough for versioned compare-and-set under `Arrive` / `Hit` / `Release` replay
- README documentation for text replay schedules, failure-report replay text, scheduler diagnostics, and limits for random-only runs; README links the cache/CAS example

### v0.3.1 — Documentation, roadmap, and hardening

Goal: documentation-only alignment plus packaging/release-doc examples and test hygiene, with **no new user-facing testing features** and **no public API changes**.

Delivered (see [CHANGELOG.md](../CHANGELOG.md) **0.3.1**):

- cross-checked README, roadmap, release process/checklist, and example docs for **.NET 10**, text replay, **Arrive / Hit / Release**, random-only failure limits, and explicit non-goals
- README **Current limitations** explicitly calls out **no `TaskScheduler` replacement** and **no binary rewriting** (still probe-driven)
- package version **0.3.1** in `src/braid/Braid.csproj`; release docs use **0.3.1** as the illustrative `$Version`
- stabilized cancellation teardown test assertion (`CancellationWhileWorkerIsHeldDoesNotDeadlock`) so replay completion vs cancellation ordering does not flake

## Planned releases

### v0.4.0 — Test-framework integration and diagnostics polish

Goal: make braid easier to use in real test suites when a race fails—without expanding scope into a full test framework or new scheduling semantics.

Scope (intentionally bounded):

- clearer failure report formatting and trace/report export cleanup where it helps copy-paste workflows
- optional xUnit-oriented helper(s) for attaching braid diagnostics (implementation may be an optional package if keeping xUnit out of the core package is preferable)
- documentation for turning flaky failures into explicit replay schedules (including honest limits: random-only runs do not synthesize a full replay schedule unless one was configured)
- one additional realistic library-concurrency example if it fits without API creep

Candidate API ideas (not committed until designed):

- `BraidReport`, `BraidTraceEntry`, `BraidSchedulerSnapshot`
- `BraidXunit.WriteFailure(...)` or similar

Exit criteria:

- failure output is easier to paste into bug reports and regression tests
- xUnit users can attach useful braid diagnostics without bespoke plumbing
- diagnostics remain deterministic and scoped to the current run
- no automatic await interception, no binary rewriting, no new implicit scheduling model

## Future ideas (not scheduled)

### Exploration and ergonomics (e.g. v0.5.x)

- additional random exploration strategies
- configurable probe selection policies
- bounded fairness options
- better seed corpus workflows; persisted failing seeds

### Analyzer / source-generator experiments (e.g. v0.6.x)

- analyzer for suspicious probe names or missing cancellation tokens in tests/examples
- source-generator helpers for strongly named probes
- opt-in diagnostics around probe naming consistency

These should stay experimental until the core runtime and diagnostics story stays simple.

## Explicit non-goals

braid will not aim to become:

- a `TaskScheduler` replacement
- a system that automatically intercepts every `await`
- a binary rewriter (no IL rewriting of user assemblies for concurrency testing)
- a distributed-system test framework
- an exhaustive model checker (no exhaustive state-space search as the default product story)
- a complete Coyote replacement, a general actor runtime, or a built-in linearizability checker

Related: braid does not hide races behind sleeps, delays, or timing assumptions; probes stay explicit. This mirrors [README.md](../README.md) (“What braid does not do”) in spirit and detail.
