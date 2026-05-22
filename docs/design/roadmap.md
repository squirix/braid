# Braid design roadmap (index)

Work plans from the current repository state through **v1.0.0**. Per-version details live in separate files below.

**Product phrase:**

> Find the interleaving. Copy the replay token. Keep the race fixed forever.

**Positioning:**

> Braid is deterministic concurrency testing for .NET libraries.

Related: [CHANGELOG.md](../../CHANGELOG.md), [README.md](../../README.md), [roadmap.md](../roadmap.md), [v0.4.0-diagnostics.md](v0.4.0-diagnostics.md).

## Version plans

| Version | Document | Status |
|---------|----------|--------|
| v0.4.0 | [v0.4.0-roadmap.md](v0.4.0-roadmap.md) | Shipped (see CHANGELOG) |
| v0.5.0 | [v0.5.0-roadmap.md](v0.5.0-roadmap.md) | Planned |
| v0.6.0 | [v0.6.0-roadmap.md](v0.6.0-roadmap.md) | Planned |
| v0.7.0 | [v0.7.0-roadmap.md](v0.7.0-roadmap.md) | Preview |
| v0.8.0 | [v0.8.0-roadmap.md](v0.8.0-roadmap.md) | Preview |
| v0.9.0 | [v0.9.0-roadmap.md](v0.9.0-roadmap.md) | Preview |
| v1.0.0 | [v1.0.0-roadmap.md](v1.0.0-roadmap.md) | Preview |

## Version table

| Version | Theme | Main outcome | Must not do |
|---------|-------|--------------|-------------|
| **v0.4.0** | Replay token + diagnostics + boundaries | `TryGetReplayText`, overlapping-probe rejection, replay-token docs | xUnit package, `ExploreAsync`, new scheduling semantics |
| **v0.5.0** | Product packaging and examples | README rewrite, 3 featured examples, Coyote/stress guidance | `ExploreAsync`, scheduler creep, xUnit in core |
| **v0.6.0** | Bounded exploration | `ExploreAsync` RFC; optional bounded search | Await interception, exhaustive model checking, breaking `RunAsync` |
| **v0.7.0** | Schedule shrinking | Shorter replay tokens from failures | Full exploration by default |
| **v0.8.0** | Test-framework integration | Optional `Braid.Xunit`, CI-friendly reports | xUnit in core package |
| **v0.9.0** | API hardening | 1.0 RC readiness | Breaking changes without plan |
| **v1.0.0** | Stable explicit-probe story | Deterministic concurrency testing 1.0 | Magic interception, Coyote replacement claims |

## Product positioning (shared)

### README hero

```text
Braid — deterministic concurrency testing for .NET libraries.

Find the interleaving. Copy the replay token. Keep the race fixed forever.
```

Lead concepts: **Braid**, **Worker**, **Hit**, **Schedule**, **Replay**, **Replay token**. **Arrive** / **Release** stay in secondary docs.

### Replay token

Canonical replay text from `ToReplayText()` / `BraidSchedule.Parse` — no separate syntax. See [replay-token-workflow.md](../replay-token-workflow.md).

### Comparison boundaries

| Approach | Braid | Stress tests | Coyote | TaskScheduler / interception |
|----------|-------|--------------|--------|------------------------------|
| Goal | Reproduce one interleaving | Find flakes under load | Systematic testing, actors | Implicit scheduling |
| Control | Explicit probes | Timing luck | Hooks, rewriting | Scheduler / IL |
| Repro | Replay token + seed | Often non-deterministic | Coyote artifacts | Varies |
| Position | Complement | Weaker determinism | Not a full replacement | Rejected for Braid |

Random-only runs do **not** synthesize a full replay token automatically.

## PR breakdown (cross-version)

| # | PR | Version |
|---|-----|---------|
| 1 | `docs-roadmap` | Index + per-version files |
| 2 | `release-v0.4.0` | 0.4.0 |
| 3–5 | replay-token README, workflow, boundaries | 0.4.0 |
| 6 | `readme-product-positioning-v0.5` | 0.5.0 |
| 7–9 | featured examples + polish | 0.5.0 |
| 10 | `release-v0.5.0` | 0.5.0 |
| 11 | `design-explore-async-rfc` | 0.6.0 |
| 12 | `explore-async-prototype` | 0.6.0 (if RFC approved) |

## Risks

| Risk | Mitigation |
|------|------------|
| API creep | Defer `BraidFailureReport`, xUnit to v0.8.0 |
| Overpromising vs Coyote | Comparison table; not a replacement |
| Magic too early | Non-goals per release |
| Replay token vs new syntax | Token = canonical text only |
| Arrive/Release too prominent | Hero = Hit + token |
| Breaking `RunAsync` | Exploration additive only |
| Random runs imply full replay | Document in workflow + failures |
| Exploration blow-up | `MaxSchedules`, `MaxStepsPerSchedule` caps |

## Explicit non-goals (v0.4.0–v0.6.0 phase)

- IL / binary rewriting
- `TaskScheduler` replacement
- Automatic interception of every `await`
- Distributed-system simulator, network partitions
- Full model checking as default
- Rider plugin before 1.0-quality core
- UI for exploration or replay editing
- Complete Coyote replacement
- Hiding races behind sleeps and delays
