# Runnatics — Race Timing Logic: Authoritative Specification

**Source of truth:** the code as read on branch `master`, 2026-06-24. Every rule cites the
governing file/method. Two parallel implementations exist (the **RFID batch pipeline** in
`RFIDImportService.cs` and the **interactive/manual path** in `ResultsService.cs`) — where
they diverge, it's flagged ⚠️.

> This document is generated from a read-only audit of the implementation. It is intended to be
> verified against the client's described rules. Where the code's behavior is ambiguous,
> inconsistent between the two implementations, or where a rule the client may expect is not
> implemented, it is flagged ⚠️ — see §13 for the consolidated list.

---

## 0. Data model (the three layers)

| Layer | Table(s) | Lifecycle | Cite |
|---|---|---|---|
| **Raw** (immutable hardware truth) | `RawRFIDReading` | Never overwritten. Clear w/ `keepUploads=true` → reset to `Pending`; `keepUploads=false` → deleted. | `RawRFIDReading.cs`; `manual-overrides.md` |
| **Override** (durable authoritative input) | `ManualTimeOverride` | Survives clear/reprocess/move (own table, no clear query touches it). Removed only by explicit revert or race-move invalidation. One ACTIVE row per (participant, checkpoint). | `ManualTimeOverride.cs`; `ManualTimeOverrideConfiguration.cs:88-92` |
| **Derived** (disposable) | `ReadNormalized`, `SplitTimes`, `Results` | Rebuilt from raw + overrides on every reprocess. | `ClearProcessedDataAsync` |

Note: `RawRFIDReading.ManualTimeOverride` (a `DateTime?` column) and the `ReadRaw` table are
**dead/deprecated** — nothing writes them (`RawRFIDReading.cs:54`; race-move comment
`ParticipantImportService.cs:2342`).

---

## 1. Raw detection ingestion

**Method:** `RFIDImportService.ParseSqliteFileAsync` (`RFIDImportService.cs:1677-1746`).

- Reads SQLite `tags` table (`id, epc, time, antenna, rssi, channel`), ordered by `time`.
- **Timestamp:** `TimestampMs` (raw int64) is stored verbatim. Conversion to
  `ReadTimeUtc`/`ReadTimeLocal` depends on `treatAsUtc`:
  - `treatAsUtc=true`: `ReadTimeUtc = FromUnixTimeMilliseconds(timestampMs).UtcDateTime`; local =
    convert via `TimeZoneId`. *(Event 30 path: `TimeZoneId="UTC"`, so `ReadTimeLocal == ReadTimeUtc`.)* (`:1698-1704`)
  - `treatAsUtc=false`: timestamp treated as local wall clock; `ReadTimeUtc = ConvertTimeToUtc(local, tz)`. (`:1705-1713`)
- **Stored:** `BatchId, DeviceId (serial), Epc, TimestampMs, Antenna, RssiDbm, Channel,
  ReadTimeLocal, ReadTimeUtc, TimeZoneId, SourceType="file_upload"`.
- **Initial status:** `ProcessResult="Pending"`, or `"MultipleEPC"` if the EPC field contains
  `,`/`|` (multi-tag read, never processed) (`:1717-1733`).
- **Immutable:** raw rows are the audit truth; the pipeline only flips
  `ProcessResult`/`ProcessedAt`/`AssignmentMethod`/`Notes`, never the timestamp.

**Timezone rules** (`context/timezone-datetime.md`): all stored UTC; convert IST↔UTC only at edges
via `Event.TimeZone` (IANA `"Asia/Kolkata"` on Linux). **Midnight-rollback fact:** any IST time
before 05:30 stores on the previous UTC day (05:29 IST = 23:59 UTC prev day) — *correct, not a bug*.

**Edge cases handled:** multi-EPC reads quarantined (`MultipleEPC`); weak signal handled later
(Phase 1, see §2).
**Gap ⚠️:** no clock-skew reconciliation across readers — assumed ≈0 (guns are calibrated from the
readers' own start-mat spikes; `event-30-gghm.md`).

---

## 2. Checkpoint assignment

Two-stage, by device→checkpoint cardinality **within the race**.

### Phase 1 — simple devices
**Method:** `ProcessAllStagingDataForRaceAsync` (`RFIDImportService.cs:3168-3643`).
- Groups reads by EPC; links EPC→Participant via `ChipAssignment` (`:3445`). **Unlinked EPC**
  (belongs to another race) → left untouched as `Pending` (`:3445-3454`).
- **RSSI guard:** `RssiDbm < -80` → `ProcessResult="Invalid"`, never assigned (`:3461-3469`).
  **Constant: −80 dBm.**
- A device mapping to **exactly 1 checkpoint** in this race = *simple* → assign immediately,
  `AssignmentMethod="DeviceMapping"` (`:3381-3383, 3491-3508`).
- A device mapping to **≥2 checkpoints** = *shared* → **deferred** to Phase 1.5, left unassigned
  (`:3386-3389, 3509-3515`).
- Unknown device / no mapping → unassigned (`:3517-3524`).
- **Batch status:** race-level batches → `completed`; event-level (RaceId=NULL) batches stay
  `uploaded` so other races can process them (`:3550-3566`).

### Phase 1.5 — shared / loop / turnaround devices
**Method:** `AssignCheckpointsForLoopRaceAsync` (`:4165-4800`) + `LoopRaceCheckpointAssigner.cs`.
- **Skipped entirely** if no device maps to ≥2 checkpoints (simple race; Phase 1 is authoritative)
  (`:4284-4297`).
- **Load window:** reads with `ReadTimeUtc >= gun − EarlyStartCutOff` AND
  `ProcessResult ∈ {Success, Pending}` (`:4412-4419`). **Constant `EarlyStartCutOff`** =
  `RaceSettings.EarlyStartCutOff` or default **10 min** (`:4207`). ⚠️ Race 49 has it set ~60 min —
  far looser than the gun gap (see §3 / §13).
- **Cross-batch dedup:** by `(Epc, TimestampMs)`, keep earliest DB `Id` (`:4453-4456`).
- **Parent/child + shared grouping:** `IdentifySharedDevices` orders a device's checkpoints by
  `DistanceFromStart` and assigns a `SharedGroupKey` so parent + child readers at one location share
  one pass counter (`LoopRaceCheckpointAssigner.cs:202-281`).
- **Pass-gap pre-dedup:** reads within `DedUpSeconds` collapse to one pass; a new pass starts after
  `PassGapThresholdSeconds` (`:4573-4613`). **Constants:** `DedUpSeconds` default **30**
  (`:4205`, mirrors `DEFAULT_DEDUP_WINDOW_SECONDS=30.0` `:30`); `PassGapThresholdSeconds` default
  **300** (`:4206`).
- **Pass-ordinal assignment** (`:4640+`, see §3 for the new gun-window logic) →
  `LoopRaceCheckpointAssigner.AssignAllCheckpoints` maps pass→checkpoint:
  - Priority 0 `PassIndexOverride` (production path) → `Checkpoints[IndexForPass(pass)]`
    (Sequential clamps extra passes to last; Cyclic wraps if `RaceSettings.HasLoops`)
    (`LoopRaceCheckpointAssigner.cs:460-465, 104-111`).
  - Priority 1 Turnaround reference (before turnaround→first, after→last) (`:467-474`).
  - Priority 2 Chronological rank within shared group (`:476-482`).
- **Mode:** `Cyclic` iff `RaceSettings.HasLoops==true`, else `Sequential` (`:4214-4216`).

**A read stays UNASSIGNED** if: unlinked EPC, RSSI<−80, unknown device, no in-window membership, or
(new) dropped as a pre-gun cross-read (§3). Unassigned reads get no `ReadingCheckpointAssignment`,
so Phase 2 excludes them (`:1968`).

**Edge cases handled:** parent/child merge to one logical gate; shared device across races
(EPC-scoped per race `:4441`); duplicate re-uploads; deferred shared assignment with full timeline.
**Gap ⚠️:** Phase 1 has **no time-window filter** at all — a simple-device race relies entirely on
Phase 2's selection (§3) to reject strays.

---

## 3. Deduplication & which read is chosen per checkpoint

### Phase 1.5 dedup (shared devices) — `LoopRaceCheckpointAssigner.DeduplicateAssignedReadings` (`:586-659`)
- Groups by `(Epc, LogicalGroup)` where checkpoints at the same `DistanceFromStart` are one logical group.
- **START** checkpoints (distance 0, or name "Start" and not "Finish") → keep **LAST** (latest read; runner leaving the mat).
- **All others (incl. Finish)** → keep **EARLIEST**.

### Phase 2 normalize — `DeduplicateAndNormalizeAsync` (`:1756-2343`)
- Loads `ProcessResult=="Success"` reads with an active checkpoint assignment, for active EPCs, race
  batches (incl. event-level) (`:1954-1976`).
- Child→parent checkpoint merge (`:2015-2036`).
- Per (participant, checkpoint) group, **bestReading**:
  - **START** checkpoint → **LAST** entry (`OrderByDescending TimestampMs`, tiebreak strongest RSSI) (`:2114-2122`).
  - **Other** checkpoints → **EARLIEST** entry (`OrderBy TimestampMs`, tiebreak RSSI) (`:2119-2122`).
- **Participant start baseline** `participantStartTimes` = **MAX (latest)** read at the start
  checkpoint per participant (`:2067-2075`), then **gun-clamped** up (BUG-27): if start-mat read is
  before the gun, baseline = gun (`:2084-2098`).
- **Monotonic guard:** removes any checkpoint whose `ChipTime <= previous checkpoint's ChipTime` in
  distance order (`:2225-2279`).

### NEW gun-window start detection (commit 1) — `AssignCheckpointsForLoopRaceAsync` (`:4639+`)
For **start-bound shared groups** (`SharedDeviceMapping.StartsAtZero`):
- **Start window** = `[gun − START_WINDOW_PRE_GUN_MINUTES, gun + START_WINDOW_POST_GUN_MINUTES]`.
  **Constants: −5 min / +15 min** (`RFIDImportService.cs:~38`).
- Start crossing = read **nearest the gun** within the window (tiebreak: first at/after gun) → pass 0.
- Reads **before** the chosen start on that group → **dropped** (left unassigned) — excludes
  cross-reads from other staggered races' guns on a shared mat.
- Reads **after** → passes 1..N (Finish chains off the corrected start).
- **No in-window candidate** → falls through to chronological (unchanged behavior; any resulting
  negative is caught downstream).

~~⚠️ **Inconsistency to note:** start selection now lives in **three** places with **different rules**~~
**RESOLVED (2026-07-03):** start selection is now a SINGLE implementation —
`StartWindow.SelectStartRead` — consumed by Phase 1.5 (`CollapseIntoPasses`) and Phase 2
(NetTime baseline + start-row normalization). See **NAMED INVARIANTS** below. The window itself is
settings-driven via `StartWindow` (`EarlyStartCutOff`/`LateStartCutOff`, SECONDS, defaults 300/1200),
not the −5/+15 min constants described above (historical).

---

## NAMED INVARIANTS — start selection & dedup (DO NOT DRIFT)

**START SELECTION INVARIANT (client-confirmed, historical rule):**
start = **LAST read of the FIRST in-window pass**, where:
- window = `[gun − EarlyStartCutOff, gun + LateStartCutOff]` via `StartWindow` (SECONDS,
  defaults 300/1200);
- pass boundary = `PassGapThresholdSeconds` (default 300s) — a later in-window blip past the
  gap is a DIFFERENT pass (e.g. the finish crossing on a shared mat), never the start;
- a same-pass read PAST the ceiling extends the pass but is not eligible to win;
- pre-floor reads never anchor/extend the first in-window pass; their exclusion and the DNS
  truth table are unchanged (validity is still "≥1 in-window read" — only WHICH read wins is
  this rule).
Implemented ONCE: `StartWindow.SelectStartRead`. Consumers: Phase 1.5
`LoopRaceCheckpointAssigner.CollapseIntoPasses` (shared devices), Phase 2
`DeduplicateAndNormalizeAsync` (NetTime baseline `participantStartTimes` + start-row
`bestReading`). Example (race 65, chip 44E0014498A0): in-window cluster 05:32:50→05:33:33,
next checkpoint 05:42:00 → start = **05:33:33**.
**Changing this rule requires explicit client sign-off.**
(History: round-2 rule was always LAST; an earliest-in-window selection introduced with the
race-65 collapse fix on 2026-07-02 was a drift, reverted 2026-07-03. The collapse fix's window
handling — pre-floor exclusion, invalid-placeholder retention — stays.)

**DEDUP INVARIANTS (round-2 originals, named):**
- **START checkpoint keeps LAST** (the runner leaving the mat).
- **ALL OTHER checkpoints (incl. Finish) keep EARLIEST** (first crossing of the gate).
These are the same rules as §3 above (`DeduplicateAssignedReadings`, Phase 2 `bestReading`
for non-start gates) — named here so they cannot drift independently of the selection rule.

---

## STATUS DEFINITIONS (#7 — client-confirmed 2026-07-03, REWRITES the old truth table)

Single source: `ResultClassifier` (`Classify` + `MandatoryDistances`), consumed by pipeline
Phase 3 AND every ResultsService path (CalculateResultsAsync, RecordManualTimeAsync,
RemoveManualTimeAsync, ComputeParticipantStatusAsync).

- **OK** (display label for stored `"Finished"` — display mapping only, `ResultStatus.ToDisplay`;
  stored values unchanged, migration is a later pass): VALID data at **ALL** mandatory gates.
- **DNF**: **ANY** mandatory gate's data missing or invalid (at least one gate valid).
- **DNS**: **NO** valid data at **ANY** mandatory gate. Invalid reads (pre-floor, out-of-window,
  discarded) are NOT data — an invalid-reads-only runner is DNS.
- **Mandatory gate set** = `{START gate (implicitly mandatory, keyed on DISTANCE — shared-mat
  safe)} ∪ {IsMandatory} ∪ ({finish} fallback when none flagged)`.
- **Start-gate validity** = the selected start crossing is inside the §NAMED-INVARIANTS window
  (`StartWindow.Contains`, boundaries inclusive). An impossible (negative) finish time = invalid
  data at the finish gate.

**DELIBERATELY REMOVED (client sign-off 2026-07-03):**
- *Finisher-safe / Row-5 keep* — no-valid-start finishers were kept Finished; now DNF.
- *Late-only-finisher keep* — start past the ceiling + finish data was Finished; now DNF.
- *Early-taint DNS* — pre-floor start + finish data was DNS; now DNF (early read = invalid data,
  not a taint); DNS only when the invalid read was their only data.
- *Manual-start discard-and-warn* (46ec16d) — an out-of-window MANUAL start used to be
  discarded (rows soft-deleted) with the runner force-flagged DNS. Now (#1 + decision 2) an
  out-of-window start — TYPED or TOGGLED — is **accepted and stored**, and #7 classification
  decides the consequence (DNF when other mandatory gates have valid data, DNS otherwise).
  Reason: the same physical situation must not produce "discard → DNS" via one UI control and
  "accept → DNF" via another; one rule everywhere, visible and revertable. Likewise a toggled
  mid/finish read violating the sequence or minimum-segment rule is accepted-but-invalid: the
  gate stays uncovered and the save succeeds with a warning naming the consequence.
Reprocessing old events (30/36/38) WILL flip some previously-Finished runners to DNF — the new
rule working, not a regression.

---

## UI REFRESH CONTRACT after recalc (#3 — API side complete 2026-07-03; UI work gated)

- **Per-participant edit / toggle save** (`POST …/manual-time` → `ManualTimeResponse`): the
  response is the COMPLETE post-recalc result, reloaded AFTER the transaction + re-rank —
  display `Status` (OK/DNF/DNS/DSQ), STORED `GunTimeMs`/`NetTimeMs` (+ formatted twins),
  `OverallRank`/`GenderRank`/`CategoryRank` (null when unranked: DNF/DNS/DSQ), `TotalFinishers`,
  and the acceptance `Warning` (#1). The UI re-renders the chip-time header card AND the edited
  participant's grid row from this payload without a second fetch. Caveat: the re-rank is
  RACE-WIDE — other rows' ranks may have shifted, so a background grid refresh remains correct
  practice after a batch of edits.
- **Bulk Process Result** (`ProcessCompleteWorkflowAsync`): returns phase counts only, by
  design — the UI re-fetches the participants grid on completion (existing behavior, unchanged).

**Edge cases handled:** shared start/finish mat cross-reads (gun window); multiple reads at a mat
(dedup LAST/EARLIEST); out-and-back via turnaround/pass-ordinal; pre-gun early-line starters (window
pre-side + gun-clamp).
**Edge cases NOT fully handled ⚠️:** a within-window earlier stray in a *simple* race; a runner with
no in-window start read (falls through, may produce negative → DNF after the §6 guard).

---

## 4. Start time (gun) handling

- **Per-race gun:** `Races.StartTime` (UTC). Staggered starts are first-class — three guns for
  event 30 (47=05:29, 48=06:02, 49=06:29 IST); **never flatten** (`event-30-gghm.md`).
- **Gun anchors start selection** via the §3 window (start crossing must be within `[gun−5, gun+15]`).
- **Validation (Phase 2):** `StartTime` null → fail (`:1791`); `|StartTime − earliest read| > 1 day`
  → fail (`:1993`); earliest read >60 min before gun (`minutesDiff < −60`) → fail (`:2004`).
  **Constants: 1 day, −60 min.**
- **Manual start entry:** overriding the start checkpoint sets its own `NetTime=GunTime` but
  **does not** recompute other checkpoints' NetTime against the new start (`manual-overrides.md:31-32`)
  ⚠️ known limitation; a full reprocess is unaffected because Phase 2 computes NetTime from the raw
  start before Phase 2.4.

---

## 5. Time calculations (all milliseconds unless noted)

| Quantity | Formula | Where |
|---|---|---|
| **ChipTime** | the UTC crossing instant (`bestReading.ReadTimeUtc`) | `ReadNormalized.ChipTime` (`:2205`) |
| **GunTime** | `ChipTime − Races.StartTime` (ms) | `:2158` |
| **NetTime** (start cp) | `= GunTime` | `:2166-2171` |
| **NetTime** (other cp) | `ChipTime − participantStart` (clamped); **negative → null** | `:2172-2188` |
| **Split cumulative** `SplitTimeMs` | `ChipTime − StartTime` (ms from gun) | `:5024-5029` |
| **Segment** `SegmentTime` | `ChipTime − previousCheckpointTime` (ms) | `:5031-5036` |
| **Legacy `SplitTime`** (TIME col) | `TimeSpan.FromMilliseconds(SplitTimeMs)`, clamped `[0, 23:59:59]` | `:5079-5097` |
| **Pace (pipeline)** ⚠️ | `(SplitTimeMs/60000) / DistanceFromStart` → min/km (**cumulative/average**) | `ResultsService.CalculateSplitTimesAsync:~173` |
| **Pace (display)** ⚠️ | `SegmentTime/60000 / segmentDistanceKm` → min/km (**per-segment**) | `PerformanceMetricsBuilder.cs:160` |
| **Speed** | `segmentDistanceKm / (SegmentTime/3600000)` → km/h | `PerformanceMetricsBuilder.cs:162`; `ResultsService:1719` |

⚠️ **Two different pace definitions coexist** (cumulative-average vs per-segment). Display builder
prefers stored `SegmentTime`, else derives from consecutive `SplitTimeMs`, else uses `SplitTimeMs`
(`PerformanceMetricsBuilder.cs:115-127`).

---

## 6. Status determination

⚠️ **Two implementations, both per-DISTANCE mandatory gates** (a detection at *any* checkpoint at a
mandatory distance satisfies it; if none flagged mandatory, the highest distance is the finish gate):

**A. Pipeline — `CalculateRaceResultsAsync` (`:2574-3054`):** classifies into **Finished / DNF / DNS**:
- **Finished:** detected at *every* mandatory distance (`:2788-2793`).
- **DNS:** not Finished **and no start-gate detection** (`:2795-2798`).
- **DNF:** started but missed ≥1 mandatory distance (`:2800-2803`).
- **Negative-time guard:** any **finish-gate** reading with `GunTime < 0` → ⚠️ **fails the WHOLE race**
  (`Status="Failed"`, returns) (`:2739-2752`). *(A planned commit 2 changes this to skip/flag the
  participant and continue.)* Negative **NetTime** only warns (`:2754-2763`); GunTime>24h only warns
  (`:2765-2774`).

**B. Interactive — `ResultsService.ComputeParticipantStatusAsync` (`:2400-2439`):** returns only
**Finished / DNF** (⚠️ **never DNS** in this path).

⚠️ **DSQ / disqualification does not exist in logic.** `Results.Status` allows `"DQ"` and
`Results.DisqualificationReason` exists, but **no code ever sets them** (confirmed across both
services). If the client expects disqualification handling, **it is not implemented.**

**Split-time negative handling:** `CreateSplitTimes` **skips** rows with negative cumulative or
negative segment (out-of-order), preserving sequence tracking (`:5038-5064`).

---

## 7. Ranking

⚠️ **Two implementations:**

**A. Pipeline:** overall by `GunTime` asc among **Finished only** (`:2847-2851`); gender via
`CalculateGenderRankingsAsync` (hardcoded `["M","F"]`, by GunTime) (`:3056-3088`); category via
`CalculateCategoryRankingsAsync` (distinct `AgeCategory`, **excludes blank/"Unknown"**, by GunTime)
(`:3090-3147`). DNF/DNS get **null** ranks (`:2908-2989`).

**B. Interactive — `CalculateResultRankingsAsync` (`:1373-1418`):** Finished only, sort by
`FinishTime` (=GunTime for autos); gender `["M","F"]`; category distinct `AgeCategory`.

⚠️ **Ties:** both use sequential `rank++` → **tied times get consecutive distinct ranks** (no
"equal rank" / no standard competition ranking with gaps). This is a behavior to confirm against
client rules.
**Split-level ranks:** `CalculateSplitTimeRankingsAsync` ranks per-checkpoint by `SplitTimeMs`,
**all participants included** (no status filter) (`ResultsService:1314-1371`).

---

## 8. Manual time overrides (durable)

- **Write:** `ResultsService.RecordManualTimeAsync` (`:1436-2061`) upserts the single active
  `ManualTimeOverride` (`ManualCrossingUtc`, `CheckpointId`, `ChosenRawReadId`, `Reason`) AND
  immediately upserts `ReadNormalized`/`SplitTimes`/`Results` and re-ranks (`:1978`) so the grid
  updates without a reprocess.
- **Input→UTC:** preferred `crossingLocalDateTime` (event-local wall clock → UTC via
  `Event.TimeZone`); legacy `finishTimeMs` paths (`:1603-1639`). **Guard:**
  `chipTimeMs <= 0 || > 86,400,000` → reject (`:1641`). **Constant: 24h.**
- **Apply on rebuild — Phase 2.4 `ApplyManualOverridesAsync` (`:2367-2573`):** runs after Phase 2,
  before splits; recomputes `GunTime = ManualCrossingUtc − gun`, `NetTime` (start cp = GunTime; else
  from start crossing, **negative→null**); collapses any duplicate rows so exactly **one crossing per
  checkpoint** survives (`:2496-2523`).
- **One-row invariant:** filtered unique index `(ParticipantId, CheckpointId) WHERE IsDeleted=0`
  (`ManualTimeOverrideConfiguration.cs:88-92`).
- **Survives clear/reprocess/move** (own table). **Revert:** `RemoveManualTimeAsync` (`:2063-2269`)
  soft-deletes the override + its `ReadNormalized`/`SplitTimes` (keyed by participant+checkpoint,
  *not* by the manual flag) and recomputes status/ranks. Only explicit revert (or race move) removes it.

⚠️ **Doc drift:** `manual-overrides.md:22` says Phase 2.4 sets `IsManualEntry=true` always; the
**current code** sets `IsManualEntry=false` when a chosen raw read is still live (§9).

---

## 9. Choose-which-read toggle (`ChosenRawReadId`)

- **Storage:** `ManualTimeOverride.ChosenRawReadId` (`long?`, **BIGINT NULL**, deliberately **not a
  FK**) (`ManualTimeOverride.cs:39`; `Add_ManualTimeOverride_ChosenRawReadId_20260621.sql`).
- **Write/validate** (`RecordManualTimeAsync:1550-1602`): the chosen read must exist, be **assigned
  to that checkpoint**, and **belong to the participant's active chip (EPC)** — else rejected.
- **Apply** (Phase 2.4 `:2441-2479`, and the immediate write): if the chosen raw read is **still
  live** → `ReadNormalized.RawReadId = chosenId`, `IsManualEntry=false` (highlights as a real read).
  If it was **hard-deleted** (clear `keepUploads=false`) → **degrade** to `RawReadId=null`,
  `IsManualEntry=true`; **timing stays correct** (from durable `ManualCrossingUtc`), only the
  read-highlight is lost. Keys off the live raw read, never a flag.
- **Revert:** same `RemoveManualTimeAsync` path as §8.

---

## 10. Race move / category change

**Method:** `ParticipantImportService.MoveParticipantToRaceAsync` (`:2301-2467`).
- **Raw reads:** **KEPT** (immutable); the participant's reads reset to `ProcessResult="Pending"`
  (+ clear `ProcessedAt/AssignmentMethod/Notes`), scoped by the participant's chip EPCs (`:2387-2394`).
- **Derived:** hard-deleted — `ReadingCheckpointAssignment` (`:2402-2407`), `Results` (`:2415-2418`),
  `SplitTimes` (`:2420-2423`), `ReadNormalized` (`:2425-2428`).
- **Overrides:** **soft-deleted** (move-invalidation — source `CheckpointId` is meaningless in
  target) (`:2435-2450`).
- Registration row moves (`RaceId`, `Status="Registered"`); source race re-ranked; target rebuilt via
  reprocess against the **target gun**.

**Edge case — moved runner carrying another race's reads:** the retained raw reads (e.g. a 21K
runner's reads) are reprocessed against the target race; the §3 gun-window prevents the source-gun
start crossing from becoming the target start.

---

## 11. Clear + reprocess

**Clear — `ClearProcessedDataAsync(eventId, raceId, keepUploads=true)` (`:3650-3848`):**
- Always deletes: `Results`, `ReadNormalized`, `SplitTimes`, `ReadingCheckpointAssignment` (scoped by
  race / participant join) (`:3669-3748`).
- `keepUploads=true` (default): raw reads → reset to `Pending`; race-level batches → `uploaded`;
  **event-level batches untouched** (`:3766-3781, 3804-3825`).
- `keepUploads=false`: raw reads **deleted**; race-level batches **deleted**; **event-level batches
  preserved** (shared across races) (`:3754-3764, 3788-3803`).
- **Never touched:** `ManualTimeOverride` (durable).

**Reprocess — `ProcessCompleteWorkflowAsync` (`:1489-1676`):** strict order
**P1 → P1.5 → P2 → P2.4 (overrides) → P2.5 (splits) → P3 (results)**. P1/P2/P3 failures are fatal
(return); P1.5/P2.5/P2.4 failures are warnings (non-fatal).

**Edge case — `keepUploads=false` then a chosen-read override exists:** the override survives but its
chosen read is gone → degrades to typed-style (§9).

---

## 12. Constants & thresholds (named, with values)

| Constant | Value | Location |
|---|---|---|
| `START_WINDOW_PRE_GUN_MINUTES` | **5** | `RFIDImportService.cs:~38` (NEW) |
| `START_WINDOW_POST_GUN_MINUTES` | **15** | `RFIDImportService.cs:~38` (NEW) |
| `EarlyStartCutOff` (load lower bound) | default **10 min** (race 49 ≈ **60** ⚠️) | `:4207` |
| `DedUpSeconds` / `DEFAULT_DEDUP_WINDOW_SECONDS` | default **30 s** | `:4205`, `:30` |
| `PassGapThresholdSeconds` | default **300 s** | `:4206` |
| RSSI reject | `< −80 dBm` | `:3461` |
| StartTime vs reads | `> 1 day` fail; `< −60 min` fail | `:1993`, `:2004` |
| Manual chip-time guard | `≤ 0` or `> 86,400,000 ms` (24 h) | `ResultsService:1641` |
| Split TIME-column clamp | `[0, 23:59:59]` | `:5084-5097` |
| Negative finish GunTime | **whole-race fail** (pre planned commit-2) | `:2739` |
| Gender buckets | hardcoded `["M","F"]` | `:3056`, `ResultsService:1396` |

---

## 13. Consolidated list of gaps / inconsistencies (read this)

1. **Dual status logic:** pipeline yields Finished/DNF/DNS; interactive
   (`ComputeParticipantStatusAsync`) yields only Finished/DNF (no DNS).
2. **DSQ not implemented anywhere** — `"DQ"` status + `DisqualificationReason` field exist but are
   never set.
3. **Dual ranking logic** (GunTime vs FinishTime sort; category Unknown-exclusion only in the pipeline).
4. **Ties get consecutive distinct ranks** (no shared rank) in both rankers — confirm vs client rules.
5. **Two pace definitions:** cumulative-average (pipeline) vs per-segment (display builder).
6. **Three different start-selection rules** (Phase 1.5 pass-ordinal gun-window NEW, Phase 1.5 Step-5
   LAST, Phase 2 MAX+clamp); simple-device races bypass the gun-window.
7. **Negative finish time fails the whole race** today (a planned commit 2 makes it skip/flag).
8. **`EarlyStartCutOff` ≈ 60 min on race 49** is the loose value that admitted cross-reads; mitigated
   by the new gun-window but the config remains loose.
9. **Doc drift:** `manual-overrides.md` understates `IsManualEntry` behavior for live chosen reads.
10. **Start-checkpoint override** doesn't recompute downstream NetTimes (only matters for the
    immediate-write path, not full reprocess).
11. **Dead artifacts:** `ReadRaw` table and `RawRFIDReading.ManualTimeOverride` column are unused.

---
---

# Plain-language version (for the client) — timing core

**1. Detections.** Every time a runner's chip passes a timing mat, the mat records the exact moment.
These raw detections are never edited or deleted during processing — they're the permanent record.

**2. Matching detections to points on the course.** Each mat is tied to one or more course points
(Start, splits, Finish). The system matches every detection to the right course point. A detection
that doesn't belong to any of this race's points is simply left unmatched and ignored.

**3. Choosing the start read (important for staggered starts).** When several races share one start
mat and start at different gun times, a runner's chip can be picked up at another race's gun while
they're near the line. So the system does **not** just take the earliest detection as the start. It
takes the detection **closest to that runner's own gun**, within a window from **5 minutes before to
15 minutes after** the gun. Detections outside that window (e.g. an hour earlier, at another race's
start) are excluded. This guarantees a 5K runner's start is their 5K gun crossing, not a stray pickup
from the 21K start.

**4. Times.** From the chosen crossings we compute:
- **Gun time** = finish moment minus the official gun (your time from the starting horn).
- **Net (chip) time** = finish moment minus the moment that runner actually crossed the start line.
- **Splits** = cumulative time to each checkpoint, and the **segment** time between consecutive
  checkpoints.
- **Pace** (minutes per km) and **speed** (km/h) are derived from those.

**5. Finish status.** A runner is **Finished** only if they were detected at every required point
(including the finish). If they started but missed a required point, they're **DNF** (did not finish).
If they were never detected at the start, they're **DNS** (did not start). *(Disqualification is not
currently automated.)*

**6. Impossible times.** A crossing that would produce a negative or impossible time (e.g. a start
pickup before the gun) is rejected rather than used, so it can't corrupt a runner's result.

**7. Ranking.** Only finishers are ranked. They're ordered by time (fastest first) for the overall
standings, and the same ordering produces the separate gender and age-category standings.
