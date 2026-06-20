# Manual Time Overrides — the three-layer model

The invariant that resolves every manual-edit edge case (display, reprocess, clear, race move):

| Layer | Tables | Lifecycle |
|-------|--------|-----------|
| **Raw** | `RawRFIDReading` | Immutable hardware truth. NEVER overwritten. (`ClearProcessedData` keepUploads=true resets it to Pending; keepUploads=false deletes it.) |
| **Override** | `ManualTimeOverride` | Durable, authoritative INPUT. Survives clear+reprocess (its own table — no clear query touches it). Preferred over raw during calculation. Removed ONLY by explicit revert or race move. |
| **Derived** | `ReadNormalized`, `SplitTimes`, `Results` | Rebuilt from raw (+ overrides applied) on EVERY reprocess/clear/move. Disposable. |

## Storage
`ManualTimeOverride` (table `ManualTimeOverrides`, SQL `db/scripts/Add_ManualTimeOverride_20260620.sql`): `EventId, RaceId, ParticipantId, CheckpointId, ManualCrossingUtc (UTC), Reason, CreatedByUserId` + AuditProperties. **Filtered unique index `(ParticipantId, CheckpointId) WHERE IsDeleted = 0`** — one active override per participant+checkpoint; a soft-delete (revert/move) releases the slot so re-override works.

Why a dedicated table and not `ReadNormalized` / `RawRFIDReading`:
- `ReadNormalized` is **always deleted** by `ClearProcessedDataAsync` → the old bug (manual edit only lived there → wiped on clear+reprocess).
- `RawRFIDReading` survives the *default* clear but not `keepUploads=false`, needs a synthetic `BatchId`, and its `ReadingCheckpointAssignment` is always deleted. Fragile.

## Write path — `ResultsService.RecordManualTimeAsync`
Inside the save transaction, STEP A-1 upserts the durable override (the single active row). `ManualCrossingUtc` is the IST→UTC conversion already computed via `Event.TimeZone`. STEP A0 still upserts `ReadNormalized` so the grid reflects immediately; the override is the source of truth for future reprocesses.

## Calculation — Phase 2.4 (the shared rebuild path)
All rebuilds funnel through `RFIDImportService.ProcessCompleteWorkflowAsync` (race-move/Process Result → `ProcessParticipantResultAsync`; clear+reprocess; manual reprocess). `ApplyManualOverridesAsync` runs at **Phase 2.4 — after Phase 2 normalize, before Phase 2.5 splits** — and for each active override upserts the `ReadNormalized` row (`ChipTime = ManualCrossingUtc`, recomputed `GunTime`/`NetTime`, `IsManualEntry = true`). Splits and Results read `ReadNormalized` downstream, so the override flows through on every path. NoTracking-safe: Phase 2 bulk-inserts (untracked); Phase 2.4 loads each row once and Update/BulkInsert — no double-attach.

## Revert — `ResultsService.RemoveManualTimeAsync` (DELETE `.../participant/{id}/manual-time?checkpointId=`)
Soft-deletes the override AND its manual `ReadNormalized`/`SplitTimes` rows, recomputes status from remaining detections (mandatory per-distance gates), re-ranks. The checkpoint reverts to its automatic read on the next reprocess — or goes truly empty (possibly flipping Finished→DNF) if it was manual-only. This is the ONLY way an override disappears; clear/reprocess never silently drop it.
**Staged UI:** the "Remove manual time" button must WARN when the checkpoint has no underlying raw read ("removes the only time at this checkpoint — runner may become DNF"). The endpoint itself does not block; the UI warns.

## Move-invalidation — `ParticipantImportService.MoveParticipantToRaceAsync`
On race move, soft-delete the participant's active overrides (step 3b). Their `CheckpointId` belongs to the source race and is meaningless in the target; left active, Phase 2.4 would re-inject a stale-checkpoint override and corrupt the target result.

## Known edge
Overriding the START checkpoint sets its own `NetTime = GunTime` but does not recompute other checkpoints' NetTime against the new start (mirrors the pre-existing `RecordManualTimeAsync` limitation). A full reprocess is unaffected because Phase 2 already computes NetTime from the raw start before Phase 2.4 adjusts the overridden checkpoint.
