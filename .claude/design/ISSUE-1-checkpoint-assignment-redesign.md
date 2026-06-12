# ISSUE 1 — Checkpoint Assignment Redesign (N-checkpoint, per CHECKPOINT-ASSIGNMENT-SPEC)

> Status: **DESIGN — awaiting approval. No code written.**
> Date: 2026-06-11 · Spec: CHECKPOINT-ASSIGNMENT-SPEC.md (Topologies A–F, EDGE-1..5)

---

## 0. Research summary (what the code actually does today)

Three places assign checkpoints; only one is authoritative:

| Path | Location | Behavior today | Fate under this design |
|---|---|---|---|
| **Phase 1** `ProcessAllStagingDataForRaceAsync` | `RFIDImportService.cs:2825+` | Assigns only simple (count==1) devices; `count>1` deferred (`3043-3046`, `3166-3173`) | Unchanged |
| **Phase 1.5** `AssignCheckpointsForLoopRaceAsync` + `LoopRaceCheckpointAssigner` | `RFIDImportService.cs:3782+`, `RFID/LoopRaceCheckpointAssigner.cs` | THE authoritative pass: deletes ALL prior assignments for the race's readings (FIX #7, `3993-4008`) and recreates them. Hardcoded count==1 / count==2 | **All changes land here** |
| **Legacy upload-time path** `ProcessRFIDImportAsync` | `RFIDImportService.cs:~1108-1389` | Per-batch pass-index assignment (`"LoopRaceSequence"`), N-checkpoint-capable but batch-scoped (no cross-batch timeline; skips extra passes, `1346-1351`) | Untouched. Its output is wiped and rebuilt by Phase 1.5's FIX #7, so it cannot conflict. Noted for awareness only |

**Callers of Phase 1.5** (signature unchanged → all unaffected): `ProcessCompleteWorkflowAsync` (`1529`), `RFIDController.cs:338` (manual trigger), `OnlineTagIngestionService`.
**Tests:** `Runnatics.Services.Tests` exists but contains **zero references** to `LoopRaceCheckpointAssigner` / `AssignCheckpointsForLoopRaceAsync` — nothing to update, and (flag) no safety net; backward-compat relies on the proof in §6.

**Why 3+ breaks today** (confirmed): turnaround filter `g.Count() == 1` (`LoopRaceCheckpointAssigner.cs:111`), shared filters `g.Count() == 2` (`:159`, `:199`), single-device `g.Count() == 1` (`RFIDImportService.cs:4148`). A 3+-checkpoint device matches none → `AssignAllCheckpoints` Case 4 drop (`:457-460`) → excluded from normalization (`RFIDImportService.cs:1953`) → zero `ReadNormalized`/`SplitTimes`.

---

## 1. Core design

**Unifying abstraction:** a device's checkpoints, ordered by `DistanceFromStart`, form `C[0..N-1]`. Pass-gap segmentation (unchanged) yields per-participant pass ordinals per shared group. Assignment = `pass ordinal → index into C`:

| Mode | Index function | Driven by |
|---|---|---|
| **Sequential** | `idx = min(pass, N-1)` — extra passes clamp to last | `RaceSettings.HasLoops != true` (false/null) |
| **Cyclic** | `idx = pass % N` — wraps | `RaceSettings.HasLoops == true` |

For `K ≤ N` passes the two modes are **identical**; they diverge only on pass N+1. Mode is resolved once per race in Phase 1.5 and stamped on each `SharedDeviceMapping` (per spec). No schema change — `HasLoops`/`LoopLength` already exist end-to-end (entity, EF config, DTOs, AutoMapper); they're just unread by assignment today.

---

## 2. Data-structure changes — `LoopRaceCheckpointAssigner.cs`

### 2a. `SharedDeviceMapping` (lines 48–61)
Remove `OutboundCheckpointId/ReturnCheckpointId/OutboundDistance/ReturnDistance`. New shape:

```csharp
public enum AssignmentMode { Sequential, Cyclic }

public class CheckpointSlot
{
    public int CheckpointId { get; set; }
    public decimal Distance { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SharedDeviceMapping
{
    public int DeviceId { get; set; }
    /// Ordered by DistanceFromStart asc (tiebreak Name, then Id). Index = pass ordinal target.
    public List<CheckpointSlot> Checkpoints { get; set; } = new();
    public AssignmentMode Mode { get; set; } = AssignmentMode.Sequential;
    public string SharedGroupKey { get; set; } = string.Empty;

    public int Count => Checkpoints.Count;
    public bool StartsAtZero => Checkpoints.Count > 0 && Checkpoints[0].Distance == 0;
    public int IndexForPass(int pass) =>
        Mode == AssignmentMode.Cyclic ? ((pass % Count) + Count) % Count
                                      : Math.Min(pass, Count - 1);
}
```

### 2b. `ReadingInput.IsOutboundOverride` (`bool?`, line 76) → `PassIndexOverride` (`int?`)
`null` = no override; else the 0-based pass ordinal precomputed by the orchestrator.

---

## 3. Method changes — `LoopRaceCheckpointAssigner.cs`

### 3a. `IdentifySharedDevices` (149–231) → `IdentifySharedDevices(List<Checkpoint>, AssignmentMode mode)`
- Primary filter `g.Count() == 2` → **`>= 2`** (line 159); child filter `== 2` → **`>= 2`** (line 199).
- Build `Checkpoints` via new `OrderCheckpointsByDistance(group)` (replaces `ResolveOutboundReturn(cps[0], cps[1])` calls at 169/205). Stamp `Mode = mode`.
- Child group key inheritance from parent (`207-213`) — unchanged. Parent + child at the same distances produce **identical ordered lists** (distance-keyed), so a shared ordinal selects the same-distance slot on both → spec's paired-device requirement holds; Step-5 distance-group dedup then merges them (unchanged).

### 3b. `ResolveOutboundReturn` (236–262) → `OrderCheckpointsByDistance`
```csharp
private List<CheckpointSlot> OrderCheckpointsByDistance(IEnumerable<Checkpoint> cps, int deviceId)
```
Order by `DistanceFromStart`, then `Name`, then `Id`. Keep the equal-distance warning (253–259) per adjacent pair. Name-based Start/Finish detection is dropped from *ordering* (distance is authoritative); Start semantics for dedup live in Step 5, untouched.

### 3c. `GenerateGroupKey` (264–274)
N-aware: join all slot names (spaces stripped) with `_` → `Start_10.5KM_Finish`; fallback `SharedGroup_{index}`. Internal grouping key only.

### 3d. `AssignAllCheckpoints` (339–469) — Case 2 rewrite (396–435)
```csharp
if (sharedDevices.TryGetValue(reading.DeviceId, out var mapping))
{
    int pass; string method;

    if (reading.PassIndexOverride.HasValue)            // Priority 0 (production-dominant path)
    { pass = reading.PassIndexOverride.Value;            method = "PassIndex"; }
    else if (hasTurnaround)                              // Priority 1: turnaround ref, generalized
    { pass = reading.ReadTimeUtc < participantTurnaround!.Value ? 0 : mapping.Count - 1;
                                                          method = "TurnaroundReference"; }
    else                                                 // Priority 2: chronological group rank
    { var rank = groupRanks.TryGetValue(reading.ReadingId, out var r) ? r : 1;
      pass = rank - 1;                                    method = "ChronologicalOrder"; }

    var slot = mapping.Checkpoints[mapping.IndexForPass(pass)];
    results.Add(new AssignedReading {
        ReadingId = reading.ReadingId, Epc = epc, DeviceId = reading.DeviceId,
        ReadTimeUtc = reading.ReadTimeUtc,
        CheckpointId = slot.CheckpointId, DistanceFromStart = slot.Distance,
        CheckpointName = slot.Name,                       // real name (was "Outbound"/"Return")
        AssignmentMethod = method });
    continue;
}
```
Cases 1 (turnaround device), 3 (single), 4 (unknown→drop+warn) unchanged. Counters/log labels updated.

**Turnaround-preservation decision (spec point 4):** since the BUG-2 fix, the orchestrator sets the pass-index override on **every** shared-device reading (`RFIDImportService.cs:4245-4261`), so Priority 0 is what production Topology-B races actually execute today — the turnaround branch is a retained fallback, not the active path. Backward compatibility (§6) therefore requires keeping Priority 0 first, NOT reordering to turnaround-first for count==2 (that would *change* current B behavior). The turnaround branch is preserved verbatim-generalized (`0` vs `N-1`, which for N=2 is exactly outbound/return) so it still protects any reading that ever lacks an override.

---

## 4. Method changes — `RFIDImportService.cs` (Phase 1.5 only)

| # | Where | Change |
|---|---|---|
| 4a | after `raceSettings` load (~3821-3824) | `var mode = raceSettings?.HasLoops == true ? AssignmentMode.Cyclic : AssignmentMode.Sequential;` |
| 4b | `IdentifySharedDevices` call (4142) | pass `mode` |
| 4c | pass-index precompute (4255-4257) | `sortedPasses[pi].IsOutboundOverride = pi == 0` → `sortedPasses[pi].PassIndexOverride = pi`; log text (4266) |
| 4d | start-bound collapse check (4198-4199) | `m.OutboundDistance == 0` → `m.StartsAtZero` |
| 4e | — | **No change** to: `sharedDeviceExists` gate (3892, `>1` already admits all shared), `singleDeviceCheckpoints` (4145-4151), turnaround identification (count==1, assigner :111), pass-gap/dedup collapse loop (4164-4234), FIX #7 wipe-and-rebuild, Step 5 dedup, Phase 2 |

Phase 1 deferral (`3166-3173`) already defers `count>1` — correct as-is.

---

## 5. Topology walkthroughs (A–F)

- **A — unique devices:** every device count==1 → Phase 1 assigns; Phase 1.5 skips (no shared device) or routes via single/turnaround. **Code path untouched.**
- **B — out-and-back (Republic Day):** Box-16 → `C=[Start, Finish]`, Box-19 → `C=[5Km, 16.1Km]`, Box-15 = turnaround (count==1, unchanged). Sequential, override `pi`: pass0→C[0], pass1→C[1], pass≥2 clamps C[1]. **Bit-identical to today** (proof §6).
- **C — 7th GGHM 21KM (broken→fixed):** Box-1 → `C=[0, 10.5, 21.1]` (N=3), Box-6 → N=4, Box-4 → N=2; children (Box-2/5/9) inherit parent group keys. `HasLoops` null → Sequential. Passes (pass-gap separated, hours apart) map ordinal→distance: pass0→0km, pass1→10.5km, pass2→21.1km; Box-6 pass0..3→2.5/7.5/13/18.5. Every group runs independently per device-group ordinal counters. → assignments → normalized → splits. **Fixed.**
- **D — multi-lap, device at 1 checkpoint:** count==1 → never enters shared logic; all passes assign to the same checkpoint (spec requirement "must NOT assign to different checkpoints" ✔). Note honestly: Step 5 + Phase 2 then keep ONE reading at that checkpoint (`(ParticipantId, CheckpointId)` group, `RFIDImportService.cs:2024`), so per-lap times are not stored — `ReadNormalized` has no lap column. Lap *counting* is out of this fix's scope (see §7-L1).
- **E — multi-lap with shared devices:** two sub-cases:
  - **E1 (recommended modeling): distinct checkpoint rows per lap** — exactly as the spec's own diagram lists them (`5Km`, `5Km(lap2)`, `Finish(10)`, `Finish(20)` are separate checkpoints; `GenerateLoopCheckpoints` already creates these from `LoopLength`). Then Box-1 → N=3 `[Start, Finish10, Finish20]`, Box-2 → N=2 `[5Km, 5Km-lap2]` and **Sequential handles it perfectly** — each lap lands on its own row, downstream needs nothing.
  - **E2: reused checkpoint rows + `HasLoops=true`** — Cyclic `pass % N` emits A,B,A,B as the spec requires, **but** Step 5 (`(Epc, distance-group)` keep-one) and Phase 2 (`(ParticipantId, CheckpointId)` keep-one) collapse lap-2's A/B; `ReadNormalized` cannot hold two rows per checkpoint. Cyclic is implemented in the assigner per spec, with this downstream limit documented (§7-L1). Production loop races should use E1 modeling.
- **F — mixed counts in one race/event:** the algorithm is already per-device (`checkpointsByDeviceId` group sizes), and Phase 1.5 runs per race over race-scoped checkpoints + race-EPC-filtered readings (`raceEpcSet`, 4048), so a 21KM/10KM/5KM event mixes freely. Single devices via Phase 1/Case 3, 2-cp via N=2, 3+ via N≥3. ✔

---

## 6. Backward-compatibility proof (A and B identical)

- **A:** no shared device → either Phase 1.5 early-exits at the `sharedDeviceExists` gate (3897-3905) or devices route through unchanged Cases 1/3. Zero modified lines execute.
- **B:** for every shared-device reading, production sets the override; old: `IsOutboundOverride=(pi==0)` → outbound=`C[0]`/return=`C[1]`. New: `PassIndexOverride=pi` → Sequential `min(pi,1)` → pass0→`C[0]`, pass≥1→`C[1]`. Same function on the same pass ordinals (pass-gap loop untouched) → **identical CheckpointId for every reading**. The shadowed fallbacks also map identically for N=2 (turnaround: `0|N-1` = outbound|return; chrono: `rank==1` → pass0). `C[0]/C[1]` ordering: old `ResolveOutboundReturn` used name-first ("Start"/"Finish") then distance; new ordering is distance-first. These differ **only** if a race names the *higher*-distance checkpoint "Start" (or lower "Finish") — inverted data that contradicts `DistanceFromStart` semantics and breaks every downstream distance-ordered display anyway. Accepted (called out for review).
- Only `count >= 3` reaches genuinely new behavior — today that path yields **zero assignments**, so there is no existing output to regress.
- `AssignedReading.CheckpointName` changes from literal "Outbound"/"Return" to the real checkpoint name — used only in logs (persisted entity has no name column). Cosmetic.

---

## 7. Edge cases (EDGE-1..5) & limitations

- **EDGE-1 (5ms duplicate, BIB 2102):** dedup-window collapse inside the pass loop (4196-4203) untouched — the 5ms read merges into pass0; 69-min gap → pass1 → 16.1Km. ✔
- **EDGE-2 (lingering at finish, BIB 1302):** reads within pass-gap of the finish pass extend the same pass (keep earliest). Official time = first crossing. ✔
- **EDGE-3 (missed checkpoint):** *cross-group* misses (the spec's example — missing another device's mat) never shift this group's ordinals: each shared group has its own counter. ✔ **Same-group miss DOES shift ordinals** (Box-1 runner missed at 10.5 → finish read becomes pass1 → labeled 10.5km): inherent to ordinal assignment on location-blind hardware; monotonic validation (2189-2245) cannot catch it (times still increase). Documented limitation **L2**; mitigation = future expected-time-window plausibility check (out of scope, flagged).
- **EDGE-4 (pre-gun start):** `readingsAfter = start − EarlyStartCutOff` (4020) untouched; start-facing first-pass keep-LAST collapse retained via `StartsAtZero` (4d). ✔
- **EDGE-5 (post-finish re-cross, gap > pass-gap):** Sequential → extra pass clamps to `C[N-1]` (finish); Step 5 keeps EARLIEST for non-start → official finish time wins, re-cross discarded. Cyclic → wraps per spec. ✔

**Limitations register:**
- **L1 — true cyclic persistence:** downstream keeps one reading per `(participant, checkpoint)`; full lap support needs lap-discriminated dedup keys + a lap column on `ReadNormalized` (schema). Out of scope; loop races use distinct-row modeling (E1).
- **L2 — same-group missed read** (EDGE-3 above).
- **L3 — no test coverage exists** for the assigner; recommend unit tests for `IndexForPass`, `IdentifySharedDevices` (N=1/2/3/4, children), and a B-topology regression fixture as part of EXECUTE.

---

## 8. Affected surface

| File | Changes |
|---|---|
| `Runnatics.Services/RFID/LoopRaceCheckpointAssigner.cs` | §2, §3 (mapping shape, mode enum, ≥2 filters, orderer, group key, Case-2 rewrite) |
| `Runnatics.Services/RFIDImportService.cs` | §4a-4d only (Phase 1.5 method) |
| Everything else | **No change**: Phase 1, legacy upload path, DeduplicateAndNormalizeAsync, split/results calc, interfaces, DTOs, controllers, UI. **No SQL.** |

---

## 9. Open items for approval

1. **Priority order** (§3d): keep PassIndexOverride dominant (= current production behavior for B) with turnaround as retained fallback — confirm, since the spec's phrasing ("turnaround-first for count==2") would alter today's B results.
2. **E1 vs E2** as the supported loop model: implement Cyclic in the assigner (cheap, spec-compliant) but document that real loop events must model laps as distinct checkpoints until L1 schema work is scoped — confirm.
3. **L3 tests:** include the new unit tests in this EXECUTE, or defer?
