# braid roadmap

braid is deterministic concurrency testing for .NET libraries (currently **.NET 10**) using explicit async probe points.

The project intentionally focuses on small, reproducible async interleavings. It does not try to replace Coyote, intercept every `await`, rewrite binaries, become a distributed-system test framework, or provide exhaustive model checking.

**Recommended next release:** **v0.5.0** — product packaging and featured examples (v0.4.0 shipped per [CHANGELOG.md](../CHANGELOG.md)).

**Detailed plans:** [design/roadmap.md](design/roadmap.md) (index) · [v0.5.0](design/v0.5.0-roadmap.md)

This roadmap matches [CHANGELOG.md](../CHANGELOG.md) through **v0.3.1** for completed work, with **Unreleased** treated as the v0.4.0 base per the design doc above.

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

See [design/roadmap.md](design/roadmap.md) for the version index, PR breakdown, risks, and version table.

### v0.4.0 — Replay token, diagnostics, runtime boundaries

Goal: make failures copy-paste friendly and document runtime probe rules—**without** new scheduling semantics or a test-framework package. Plan: [design/v0.4.0-roadmap.md](design/v0.4.0-roadmap.md).

Scope (see CHANGELOG **Unreleased** + design doc):

- `BraidRunException.TryGetReplayText`
- reject overlapping probe waits on the same logical worker (flowing child tasks)
- replay-token terminology and [replay-token-workflow.md](replay-token-workflow.md)
- README replay-token section; runtime-boundary documentation
- doc-only xUnit guidance (`ITestOutputHelper` + `ToString()` / `TryGetReplayText`) — **no** `Braid.Xunit` package in v0.4.0

Deferred from earlier roadmap drafts: `BraidFailureReport`, `Braid.Xunit` → **v0.8.0** preview.

### v0.5.0 — Product packaging and examples

Goal: README restructure, three featured examples, when-to-use guidance. Plan: [design/v0.5.0-roadmap.md](design/v0.5.0-roadmap.md).

Examples: lost update, cache/CAS (existing), cancellation before observation. Stable API; no `ExploreAsync` yet.

### v0.6.0 — Bounded exploration (design + optional ship)

Goal: `ExploreAsync` RFC and optional bounded search with replay token on failure. Explicit probes only. Plan: [design/v0.6.0-roadmap.md](design/v0.6.0-roadmap.md).

## Future preview (not scheduled in detail)

| Version | Direction |
|---------|-----------|
| v0.7.0 | [Schedule shrinking](design/v0.7.0-roadmap.md) |
| v0.8.0 | [`Braid.Xunit`](design/v0.8.0-roadmap.md) (optional package) |
| v0.9.0 | [API hardening / RC](design/v0.9.0-roadmap.md) |
| v1.0.0 | [Stable 1.0](design/v1.0.0-roadmap.md) |

### Other ideas (experimental)

- analyzer for probe naming or missing cancellation tokens
- source-generator helpers for strongly named probes

Stay experimental until the core replay-token and exploration story is stable.

## Explicit non-goals

braid will not aim to become:

- a `TaskScheduler` replacement
- a system that automatically intercepts every `await`
- a binary rewriter (no IL rewriting of user assemblies for concurrency testing)
- a distributed-system test framework
- an exhaustive model checker (no exhaustive state-space search as the default product story)
- a complete Coyote replacement, a general actor runtime, or a built-in linearizability checker
- a Rider plugin or UI before core 1.0-quality story

Related: braid does not hide races behind sleeps, delays, or timing assumptions; probes stay explicit. This mirrors [README.md](../README.md) (“What braid does not do”) in spirit and detail.
