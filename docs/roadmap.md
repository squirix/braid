# braid roadmap

braid is deterministic concurrency testing for .NET libraries (currently **.NET 10**) using explicit async probe points.

The current stable product intentionally focuses on small, reproducible async interleavings. Future controlled-runtime work is opt-in and phased; the project does not promise unrestricted CLR-wide scheduling control, binary rewriting, distributed-system simulation, or exhaustive model checking as the default product story.

**Recommended next release:** **v0.6.0** — bounded exploration design and optional implementation (v0.5.0 shipped per [CHANGELOG.md](../CHANGELOG.md)).

This roadmap matches [CHANGELOG.md](../CHANGELOG.md) for completed work and keeps future direction intentionally high level.

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

### v0.4.0 — Replay token, diagnostics, runtime boundaries

Goal: make failures copy-paste friendly and document runtime probe rules without new scheduling semantics or a test-framework package.

Delivered (see [CHANGELOG.md](../CHANGELOG.md) **0.4.0**):

- `BraidRunException.TryGetReplayText`
- rejection for overlapping probe waits on the same logical worker
- replay-token terminology and [replay-token-workflow.md](replay-token-workflow.md)
- runtime-boundary documentation
- doc-only xUnit guidance through `ToString()` / `TryGetReplayText`

### v0.5.0 — Product packaging and examples

Goal: make the current explicit-probe product easier to understand and try.

Delivered (see [CHANGELOG.md](../CHANGELOG.md) **0.5.0**):

- README restructure around install, quick start, replay tokens, and when-to-use guidance
- three featured examples: lost update, cache/CAS, and cancellation before observation
- walkthrough docs for the new featured examples
- package metadata alignment

## Planned releases

This section is intentionally high level. Detailed working plans stay local/private until they are stable enough for contributor-facing documentation.

### v0.6.0 — Bounded exploration (design + optional ship)

Goal: `ExploreAsync` RFC and optional bounded search with replay token on failure. Explicit probes only.

## Future preview

Future releases may include schedule shrinking, optional test-framework integration, API hardening, and a stable 1.0 explicit-probe story.

### Other ideas (experimental)

- analyzer for probe naming or missing cancellation tokens
- source-generator helpers for strongly named probes

Stay experimental until the core replay-token and exploration story is stable.

### Controlled runtime track

Longer-term automatic scheduling work remains experimental and opt-in. Public docs should describe it only after the design is stable enough for contributor-facing discussion.

## Explicit non-goals

braid will not aim to become:

- an unrestricted CLR-wide scheduler without opt-in boundaries
- a system that silently intercepts every `await` in arbitrary code
- a binary rewriter (no IL rewriting of user assemblies for concurrency testing)
- a distributed-system test framework
- an exhaustive model checker (no exhaustive state-space search as the default product story)
- a general actor runtime or a built-in linearizability checker
- a Rider plugin or UI before core 1.0-quality story

Related: braid does not hide races behind sleeps, delays, or timing assumptions; probes stay explicit. This mirrors [README.md](../README.md) (“What braid does not do”) in spirit and detail.
