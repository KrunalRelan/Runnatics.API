# Runnatics.API — Context Index

> **Read `context/invariants.md` FIRST** — the meta-lesson (verify diagnoses against prod data before acting) and the recurring failure patterns that have bitten us repeatedly.
> After completing a task, append a dated entry to `context/session-log.md` and update the relevant topic file below.

This file is a router. Detailed context lives in focused files under `.claude/context/`.

## Topic files

| File | What's in it | Read when |
|------|--------------|-----------|
| [context/invariants.md](context/invariants.md) | **Read first.** Meta-lesson + cross-cutting recurring failure patterns (dual implementations, insert-only skip-guards, enum-vs-DB-string, subset-evaluated guards, bib reuse). | Always, before any task. |
| [context/timezone-datetime.md](context/timezone-datetime.md) | UTC storage rule, the IST pre-05:30 midnight-rollback fact, Event.TimeZone conversion path, the kind-less-datetime serialization gotcha, manual-time entry rules. | Any time/date/timezone work, manual-time, splits, gun times. |
| [context/event-30-gghm.md](context/event-30-gghm.md) | Event 30 (7th GGHM) specifics — RaceIds 47/48/49 and their STAGGERED start times (do not flatten to one gun). | Working on event 30 / GGHM data or timing. |
| [context/ef-tracking-invariants.md](context/ef-tracking-invariants.md) | Global NoTracking double-attach rule + fixes; "second operation on this context" concurrency rule (await/sequence; background work on its own DI scope). | Any read-then-write service path, transactions, recalc/re-rank, fire-and-forget. |
| [context/manual-overrides.md](context/manual-overrides.md) | Three-layer model (raw / durable override / derived). `ManualTimeOverride` table survives clear; Phase 2.4 applies it on every rebuild; explicit revert + race-move-invalidation. | Any manual-time work, clear/reprocess, race move, or "why did a manual edit survive/disappear". |
| [context/architecture.md](context/architecture.md) | Project overview, key architectural decisions, layer map, entity/service/controller inventory. | Onboarding / locating where code goes. |
| [context/session-log.md](context/session-log.md) | Full dated history of every fix/diagnosis (the former CONTEXT.md body). | Tracing why a past change was made; appended after each task. |
| [context/queued-cutoff-datetime-audit.md](context/queued-cutoff-datetime-audit.md) | QUEUED combined audit: (A) EarlyStartCutOff/LateStartCutOff seconds-vs-minutes unit fix + whole-group sweep; (B) full datetime=UTC every-column audit. Gated behind race-49 StartTime fix + commit-1 verification. Seeded pre-findings inside. | Before touching RaceSettings cutoff consumption or doing the datetime sweep. |
| [context/queued-ui-starttime-fix.md](context/queued-ui-starttime-fix.md) | QUEUED (UI repo, gated): edit-race form must load/send the correct UTC StartTime on every save so a settings-only save can't clobber the gun. Server stays permissive (gun freely editable); the Option-1 server lock-down was reverted. | Why race StartTime reverts on settings save; any edit-race/gun UI work. |

## TODO — incremental migration
Distill these topical references OUT of `session-log.md` into their own focused files (the raw history stays in session-log.md until then):
- `context/rfid-pipeline.md` — Phase 1→3 processing, normalization, dedup, checkpoint assignment (loop/shared-device), skip-guards, clear/forceReprocess gate.
- `context/race-move.md` — participant race-move model (re-register + reprocess-from-raw), what derived data is invalidated, why Process Result owns the rebuild.
