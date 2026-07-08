## Session Log

_Use this section to log what each agent built during the current session._

### 2026-06-30 — Manual-edit path now applies the valid-start floor + is negative-safe (parallel-impl catch-up) — Opus

**⚠️ PUSHED TO MASTER UNVERIFIED (user-authorized).** Build green. The manual-time edit (`ResultsService.RecordManualTimeAsync`) was the divergent parallel impl that never got the floor / negative-safe fixes the pipeline already has: it rejected ANY crossing at/before the GUN (`chipTimeMs <= 0`) and hard-errored (`ErrorMessage` → 500). So a valid slightly-early start (06:07:41, after the floor) AND a pre-floor stray (06:06:41) both 500'd.

**Fix (routes through the SAME `StartWindow` helper — no 3rd copy):**
- Race load now `Include(RaceSettings)`; compute `isStart = editedIndex == 0` + `(floor, ceiling) = StartWindow.For(gun, EarlyStartCutOff, LateStartCutOff)`.
- Replaced the `chipTimeMs <= 0` reject with checkpoint-aware logic (kept the `> 24h` upper guard for both):
  - **Start + in-window** → VALID (accept even if `chipTimeMs < 0`; clamp to 0 = gun-time baseline, BUG-27). → 06:07:41 accepted.
  - **Start + out-of-window** → `DiscardOutOfWindowStartAsync`: soft-delete any override/RN/split at the start checkpoint, force **DNS** (pipeline case-2), re-rank via shared `RankCalculator`, return **SUCCESS + Warning** (new `ManualTimeResponse.Warning`). No error. → 06:06:41 discarded + DNS.
  - **Non-start + pre-gun** → clean validation message ("A mid-race/finish crossing can't be before the race start time (gun)") → controller maps "before the race start" → **HTTP 400** (added the keyword to `RFIDController` RecordManualTime's 400 conditions; it already 400s "invalid"/"after race start").
- Files: `ResultsService.cs`, `ManualTimeResponse.cs`, `RFIDController.cs`.
- **Note:** the controller already mapped "invalid"/"after race start" → 400, so the reported 500 was likely a pre-`d6232e1` build; this makes the new non-start message 400 too.
- **Verify (prod):** start 06:06:41 → success/DNS/"not found", no error; start 06:07:41 → accepted, normal times; non-start pre-gun → 400 not 500; manual path + pipeline agree on status/times.

### 2026-06-30 — Item C: Phase-2 StartTime abort guard now respects the valid-start floor (race 62 / event 36) — Opus (UNCOMMITTED, working tree)

**Symptom:** race 62 (event 36) reprocess aborted with "Race.StartTime … is more than 1 hour AFTER the earliest reading". Confirmed (via device mapping) the "earliest reading" was a ~87-min-early setup/test-tag stray on the shared Start/Finish mat (hw `00162511809d`/`0016251182c3` = devices 11/10; gun 06:30 IST, floor 06:29:50 @ cutoff 10s).

**Root cause:** the Phase-2 guard (`DeduplicateAndNormalizeAsync`, `RFIDImportService.cs:~1993`) computed `earliestReading = rawReadings.Min(...)` with **no floor**, so a pre-floor stray became "the earliest" and tripped `minutesDiff < -60` → whole-race abort — before the floor could ignore it.

**Fix (build green, uncommitted):**
- Exclude pre-floor reads via the shared `StartWindow.For(...)` (same helper as selection/status) BEFORE computing `earliestReading` → earliest is now the earliest **post-floor** read. Race 62 → real 06:xx → no abort; 05:03 strays ignored.
- KEPT `daysDiff > 1` whole-race abort (genuine >1-day StartTime misconfig).
- DOWNGRADED `minutesDiff < -60` from whole-race abort → logged **warning** + surfaced via new `DeduplicationResponse.Message` (added) → `ProcessCompleteWorkflow` adds it to `response.Warnings`. Rationale: post-floor reads are within EarlyStartCutOff of the gun by construction; a large negative = intentionally-wide window, not bad data → must not abort.
- Files: `RFIDImportService.cs`, `DeduplicationResponse.cs`.
- **Depends on** the valid-start-window/floor code (commit `83e4b3f`, on master) — this guard fix + that floor logic must ship together; confirm event 36's env is on latest master.

**⚠️ SEPARATE LATENT FINDING — Devices duplicate hardware (NOT fixed this pass):** the Devices table has multiple rows for one hardware MAC (`0016251182bc` → Ids 2/9/13/14; `00162511ebf3` → 1/8), some active, some deleted. The read→device lookups correctly filter `IsActive && !IsDeleted` (so deleted rows are excluded), BUT among multiple **active** rows sharing a MAC the lookup is last-wins: `deviceLookup[mac] = device.Id` (Phase 1.5 ~:4240) / `deviceSerialToId` (Phase 1 ~:3317) → could map a read to the wrong active device/checkpoint. **Race 62 unaffected** (its MACs are unique). For a later data-integrity pass: unique constraint on active `DeviceMacAddress`, or a deterministic lowest-Id tiebreak in the lookup.

### 2026-06-30 — Stored ranks via one RankCalculator + RankOnNet/per-view basis; all surfaces read stored ranks — Opus

**⚠️ PUSHED TO MASTER UNVERIFIED (user-authorized).** Build green; pushed before the verification below ran. **Pending:** per-view query (`SortByOverallChipTime<>SortByCategoryChipTime`); reprocess; confirm admin grid + public site + export show identical order per view; reprocess vs manual-edit produce same ranks; grid order == rank number. Changes ranks for every race + reorders live published results — fix forward on master if a problem surfaces.

Root-cause fix for the "grid order vs rank-number disagree" bug + the 3-way divergence (stored ranks by gun; admin grid ordered by NetTime showing gun ranks; public/export computed their own order off a *different* setting).
- **New `RankCalculator`** (single source of truth): `AssignRanks(finished, overallBasis, categoryBasis)` — Overall+Gender by overall basis, Category by category basis; **Gender = M/F only** (non-M/F → null GenderRank); Category skips blank/"Unknown"; tiebreak **primary time → other time → ParticipantId** (not bib — reused/non-unique). `ResolveBasis(effective, rankOnNetDefault)` — per-view `SortByOverallChipTime/SortByCategoryChipTime` defaulting to `EventSettings.RankOnNet`. `ApplyStoredRanksAsync(repo, event, race, user)` — loads Finished+Participant, resolves basis, assigns, BulkUpdate.
- **Both calc paths call it:** pipeline `CalculateRaceResultsAsync` + `ReprocessParticipantsAsync`; interactive `ResultsService.CalculateResultRankingsAsync` now delegates. **Removed** the pipeline `CalculateGenderRankingsAsync`/`CalculateCategoryRankingsAsync` (gun-only) — no more divergent ranking impls.
- **All 3 surfaces read stored ranks:** admin grid `GetLeaderboardAsync` overall branch → `OverallRank` (was `OrderBy(NetTime)` while showing gun rank — the screenshot bug); public site podium/category/overall → stored `OverallRank`/`CategoryRank` (+ display stored rank as the number; labels aligned via `ResolveBasis`); export category sheet → `CategoryRank` (overall sheet already used `OverallRank`).
- **Behavior change to note:** public/export basis default when `SortBy*ChipTime` is null flips from `?? true` (chip) to `?? RankOnNet` — matches event-level intent; for event 30 (RankOnNet=true) identical. `CalculateResultsResponse.CategoriesProcessed` now 0 (cosmetic; ranking no longer returns a category count).
- **Part 2 (hh:mm:ss + numeric column sort + Start-as-clock + default-order-by-rank):** UI repo, gated — API already exposes ms + hh:mm:ss + stored ranks. `FormatTime` already `@"hh\:mm\:ss"`.
- **Verify (working tree, before master):** run `SELECT COUNT(*) … SortByOverallChipTime<>SortByCategoryChipTime` (settles per-view usage); reprocess; **admin grid + public site + export show identical order per view**; reprocess vs manual-edit produce same ranks; grid order == rank number; ties deterministic (ParticipantId); only Finished ranked.

### 2026-06-30 — Valid-start window [floor, ceiling] from settings + DNS truth-table — Opus

**⚠️ PUSHED TO MASTER UNVERIFIED (user-authorized).** Build green (0 errors); pushed to `master` at the user's explicit request BEFORE prod verification. **Pending prod checks:** race 49 StartTime must be set to `00:59` UTC; reprocess 47/48/49; the regression gate = "any 47/48 FINISHER with earliest start-gate read < their floor?" (zero rows → safe; rows → genuine gun-jumper vs too-tight floor); 2133 → 06:29 start ~62 min; 05:29-only → DNS "not found". If a problem surfaces, fix forward on master.

Replaces the hardcoded gun-window (commit `d02a01c`) with a settings-driven valid-start window and a precise DNS rule. Changes status classification for EVERY race.

- **Window:** `floor = gun − EarlyStartCutOff` (default 300s), `ceiling = gun + LateStartCutOff` (default 1200s), both SECONDS, `>0` guard. **`LateStartCutOff` wired up** (was unused) as the ceiling. Removed the `START_WINDOW_PRE/POST_GUN_MINUTES` constants.
- **Valid start = EARLIEST start read in [floor, ceiling].**
- **P1.5 (shared, `RFIDImportService`):** window=[floor,ceiling]; start=earliest in-window; no in-window → chronological fallthrough keeps earliest as an INVALID placeholder. **Load lower-bound REMOVED** (load all Success+Pending for the race batches) so pre-floor/early reads are RETAINED for Phase 3 (the key "retain-not-drop" requirement — load-bound=floor would have broken case 2).
- **P2 (simple):** `Include(RaceSettings)`; start row = earliest in-window else earliest-available placeholder; NetTime baseline = earliest valid (gun-clamped) else **gun** (late finisher nets from gun).
- **P3 (status):** `Include(RaceSettings)`; per-participant earliest start-gate read drives the truth table:
  - earliest start read **< floor** → **DNS** (case 2; even with finish data).
  - earliest in **[floor,ceiling]** → Finished if all mandatory covered, else DNF.
  - no valid start (late-only or no read) **+ finisher** → **kept Finished** (case 3 + Row-5 ruling: finisher = ran).
  - no valid start **+ non-finisher** → **DNS** (case 1).
- **Display (`ResultsService.LoadCheckpointTimesAsync`):** start checkpoint shows "not found" (blank) when the start read is ∉ [floor,ceiling].
- **Row-5 ruling:** no start read + finisher → KEEP (full mandatory coverage = demonstrably ran; missing start = reader miss).
- **Consistency (divergent-impl guard):** the window math lived in 4 copies (P1.5/P2/P3 + ResultsService display) — collapsed into ONE shared helper `Runnatics.Services.StartWindow.For(gun, early, late)` (defaults 300/1200, `>0` guard). All 4 sites call it, so **status and display can't drift** (the recurring dual-implementation failure mode). New file `StartWindow.cs`.
- **Verify (working tree, then prod):** case 2 (early+finish → DNS, "not found"); case 1 (no start, non-finisher → DNS); race 49 2133 (06:29 ∈ [06:24,06:49] → valid, ~62 min); 05:29-only → DNS "not found"; defaults 300/1200; **47/48 regression — no in-window finisher flips to DNS; late finishers kept**. Prereq: race 49 StartTime = 00:59 in prod.

### 2026-06-28 — Result-calc fixes A+B+D (EarlyStartCutOff unit, negative-finish flag-not-500, load-gate floor) — Opus

**⚠️ PUSHED TO MASTER UNVERIFIED (user-authorized).** Commit 1 (gun-window) + commit 2 (A+B+D) + commit 3 (docs) pushed straight to `master` at the user's explicit request BEFORE the prod a–e verification ran. Prod check still PENDING: (a) race 49 process-all completes; (b) the 7 runners resolve to a 06:29 start or flag DNF (2133 → 06:29, ~62 min); (c) negative-finish → flagged DNF not 500; (d) **47/48 regression diff — the gate**; (e) load window ~5 min not 5 h. Prereq: race 49 StartTime must be `00:59` in prod. If a–e surface a problem, fix forward on master.

Scope of a full result-calc audit; A+B+D approved, C deferred, E–H held. All in `RFIDImportService.cs`, build green.
- **A — EarlyStartCutOff unit:** column is SECONDS (UI default 300) but was consumed via `AddMinutes` (60× too wide = 5 h). Fixed: `:4220` default `10`→`300`; load consumer `AddMinutes(-earlyStartCutOff)`→`AddSeconds(-loadCutoffSeconds)`; log `min`→`sec`. Sole consumer (grep-confirmed). `LateStartCutOff` confirmed UNUSED (no consumer) — left as-is. `DedUpSeconds`/`PassGapThresholdSeconds` already correct seconds.
- **B — negative finish GunTime no longer 500s the whole race:** was `Status="Failed"; return` (one bad runner blocked ALL results). Now: collect `flaggedNegativeFinishIds`, drop them from `finishReadings`, classify them DNF (new first branch in the Finished/DNF/DNS loop), and surface `(N flagged DNF: negative finish time)` in `response.Message`. Race processes everyone else.
- **D — load gate can't starve the gun-window:** `loadCutoffSeconds = Math.Max(earlyStartCutOff, START_WINDOW_PRE_GUN_MINUTES*60)` so the outer load window (P1.5) never undercuts commit 1's inner gun-window pre-side. At default 300s they're equal.
- **Deferred C:** P2 `daysDiff>1`/`minutesDiff<-60` whole-abort guards (`:2012`/`:2023`) — same class as B but larger/order-dependent; separate pass if they still fire after A.
- **Held E–H:** tie policy (goes with RankOnNet work), DSQ (unimplemented feature), two pace defs (display), `>0`-guard 0-vs-null (kept).
- **Verify (prod):** EarlyStartCutOff 300 → 5 min pre-gun (not 5 h); a negative-finish runner flags DNF and the race still ranks everyone else; load gate ≥ gun-window pre-side. **Prereq:** race 49 StartTime must be `00:59` in prod before verifying; don't re-save race-49 settings via the buggy UI form (see `queued-ui-starttime-fix.md`).

### 2026-06-28 — Gun-window start fix (commit 1, uncommitted) + cutoff/datetime audit queued — Opus

**Commit 1 (DONE in working tree, NOT committed, NOT prod-verified):** gun-anchored START selection for staggered-start shared mats. `RFIDImportService.AssignCheckpointsForLoopRaceAsync` (Phase 1.5) — for start-bound shared groups (`StartsAtZero`), the Start crossing is now the read NEAREST the gun within `[gun − START_WINDOW_PRE_GUN_MINUTES(5), gun + START_WINDOW_POST_GUN_MINUTES(15)]` (tiebreak first-at/after-gun); reads before the chosen start are dropped (left unassigned); reads after keep passes 1..N. No in-window candidate → falls through to existing chronological (unchanged). New named consts near `:30`. Replaces the gun-blind "earliest read = pass 0 = Start" at `:4640+`. Fixes the 2133 case (5K runner with a 05:29 21K-gun cross-read on the shared Start/Finish mat being chosen as the 5K start). Build ✅ 0 errors. Tests NOT run — only .NET 10 runtime installed locally, net8.0 testhost fails discovery (environment, not the change). **Awaiting user prod reprocess of race 49 + 47/48 regression diff before commit 2 (negative-time guard: skip/flag, don't 500 the race).**

**Part B docs (DONE):** strengthened `timezone-datetime.md` with the explicit invariant ("every datetime column in every table = UTC; timezone is separate metadata; convert only at edges") + a "Verification status" section listing the columns verified-UTC this session (StartTime, ReadTimeUtc, ChipTime, ManualCrossingUtc) and marking the full every-column sweep PENDING.

**Queued (NOT started — gated behind race-49 StartTime fix + commit-1 verify):** `context/queued-cutoff-datetime-audit.md` — combined Part A (EarlyStartCutOff/LateStartCutOff seconds-vs-minutes unit fix + whole-group `*CutOff/*Seconds/*Threshold` sweep) + Part B (full datetime=UTC code audit). **Confirmed:** `EarlyStartCutOff` column is seconds (`DB Tables.md:485`, UI default 300) but consumed via `AddMinutes` (`:4412`) → 60× too wide (300s read as 5h). **Seeded pre-findings:** (1) the `> 0` guard (`:4218`,`:4220`) makes BOTH 0 AND null fall to in-code defaults — null-OR-zero path, and `0` ambiguously means "use default" not "zero window"; (2) `LateStartCutOff` (=1200 prod) is defined/mapped but NO consumer found in `RFIDImportService` — OPEN: unused vs consumed-elsewhere, confirm before calling it a bug.

**Still the real blocker:** race 49 `Races.StartTime`. CONFIRMED `00:59` (06:29 IST, correct) earlier this session — BUT a LATER prod dump (Events/RaceSettings query) showed race 49 StartTime = `2026-05-09 23:59:00` (05:29 IST, WRONG — identical to race 47's value AND to `Event.EventDate`), `UpdatedAt 2026-06-23 09:32:08`. So there is before/after evidence it CHANGED. Do NOT verify commit 1 until the current live gun is re-confirmed `00:59`. No prod DB access from here.

**Races.StartTime write-path audit (DONE 2026-06-28, read-only) — findings still valid; CONCLUSION corrected below:** `Race.StartTime` has **NO direct `.StartTime =` assignment anywhere** — it is written ONLY via AutoMapper `CreateMap<RaceRequest, Race>()` (`AutoMapperMappingProfile.cs:212-217`), through exactly two paths: race CREATE `RaceService.cs:110` `_mapper.Map<Race>(request)`, and race UPDATE `RaceService.cs:436` `_mapper.Map(request, raceEntity)` (in `UpdateRaceEntity`). The update overwrites StartTime from `request.StartTime`, INCLUDING saves that only touch RaceSettings (same modal/DTO → same `UpdateRaceEntity`). `RaceRequest.StartTime` is non-nullable `DateTime` (`RaceRequest.cs:16`) vs entity `DateTime?`. **NO server-side EventDate→StartTime copy exists** (grep clean across Event/Import/bulk). So the `23:59` did not come from server code; it came from the **inbound `RaceRequest`** on the 2026-06-23 09:32 save.

**⚠️ DIRECTION CORRECTED (2026-06-28) — do NOT lock the server.** Initial conclusion was "harden the server (Ignore StartTime + dedicated endpoint)". That was WRONG for the requirement: **StartTime/EndTime must stay FREELY editable via the normal edit-race path** — the gun is a normal field; users change race start/end anytime. The real bug is the **UNINTENDED overwrite** (a settings-only save carrying a wrong gun), NOT that the gun can change. **An Option-1 server lock-down WAS implemented then fully REVERTED** the same day (AutoMapper Ignore + `UpdateRaceStartTimeAsync` + `start-time` endpoint + DTO + test — all reverted; only commit-1 `RFIDImportService.cs` + docs remain modified). **REAL FIX = UI (separate repo, gated on auth/push):** the edit-race form must load StartTime/EndTime as explicit UTC (`Z`) and send the race's actual value on EVERY save (settings-only included), never defaulting to `EventDate` / never mis-serializing (the documented kind-less-datetime trap). **Server confirmed permissive + non-corrupting:** edit-race maps `request.StartTime` (DateTime) → `Race.StartTime` (DateTime?) verbatim with no value converter and no IST re-localization — it stores the incoming instant as-is, so a correct UTC value is preserved; correctness depends on the client sending proper UTC (the UI fix). Timing handles early/late starts via `EarlyStartCutOff`/`LateStartCutOff` (queued seconds-unit fix), NOT a frozen gun. See `queued-ui-starttime-fix.md`.

### 2026-06-20 — PART 2: one-button "Save & Process Result" on manual-time edit — Opus EXECUTE

**Goal:** editing a manual time should save AND recompute the participant + re-rank the race in one action, without a separate Process Result click. **Research finding (decisive):** `RecordManualTimeAsync` (the `addManualTime` PUT) ALREADY recomputes this participant's RN/SplitTimes/Results from the new ChipTime and calls `CalculateResultRankingsAsync` (re-ranks the WHOLE race overall/gender/category) — all inside ONE transaction in ONE request. So the per-participant recompute + whole-race re-rank is already atomic; no second call needed → both prior bugs (NoTracking double-attach, "second operation on this context") are avoided by construction. Chaining the full `ProcessParticipantResultAsync` (→ `ProcessCompleteWorkflowAsync`, re-normalize from raw) was REJECTED: it always writes `IsManualEntry=false` (RFIDImportService.cs:2198) and, where a raw read also exists at the checkpoint, would add a competing automatic `ReadNormalized` that `LoadCheckpointTimesAsync` (picks `OrderBy(ChipTime).First()`) could surface over the manual time — i.e. clobber risk + heavier. **User-confirmed:** Option A (in-save recalc), keep the header Process Result button (it serves the race-move/category edit form + manual reprocess-from-raw). **Changes:** (API) `RecordManualTimeAsync` now calls `CalculateResultRankingsAsync` UNCONDITIONALLY (was gated on `Finished`) so a manual edit that flips a runner Finished↔DNF re-ranks the whole race and closes/opens the gap — the ranker re-ranks only the Finished set via BulkUpdate, correct+cheap regardless. (UI) manual-time row ✓ relabeled "Save & Process Result"; success snackbar says "Result recalculated and race re-ranked." The row already chained save→server recalc/re-rank→refresh; no second request added. **Files:** `ResultsService.cs` (API); `ParticipantDetail.tsx` (UI). Builds ✅ API+UI, tests ✅ 19/19. **Test target:** edit a manual time on race 49 → one click → time saves + result/ranks update, whole race re-ranks (not just the one runner).

### 2026-06-20 — Split Times & Checkpoint Analysis: 3 fixes (child rows / manual-time TZ / manual-time 500) — Opus EXECUTE

**Target:** participant Split Times & Checkpoint Analysis screen, 7th GGHM (EventId 30; 21.1 Km = RaceId 47; race enc `K_h0cSgS23MmRFBkJhs3Kg`). Three separable issues. Prod facts confirmed via sqlcmd: `Event.TimeZone='Asia/Kolkata'` (NOT "UTC" as the brief assumed → no TZ data-fix needed); Race 47 `StartTime='2026-05-09 23:59:00'` (near-midnight gun → crossings land on May 10, hence the editor needs a DATE); ReadNormalized.ChipTime is a stored UTC instant and `GunTime = ChipTime − StartTime` (verified: ChipTime `2026-05-10 03:09:15` → GunTime 11,415,566 ms).

**ISSUE 1 — child checkpoints rendered as blank "-" rows (API, display-only).** Root cause: `ResultsService.LoadCheckpointTimesAsync` (`:948`) returned EVERY active checkpoint incl. child/paired-reader ones. Discriminator (authoritative, used in `RFIDImportService.cs:1824/2358`, `CheckpointsService.cs:530`): child ⇔ `ParentDeviceId > 0`; parent ⇔ `ParentDeviceId == null || == 0`. **Verified in DB** for event 30: every named checkpoint has `ParentDeviceId NULL`, every unnamed child has it set (no real checkpoint would be wrongly hidden). Fix: added `(c.ParentDeviceId == null || c.ParentDeviceId == 0)` to the query. `LoadCheckpointTimesAsync` is private, single caller (`:896`, builds `response.CheckpointTimes`), downstream of ranking — display only, no timing/normalization impact.

**ISSUE 3 — manual-time save 500 "SplitTimes … already being tracked" (API; same NoTracking family as race-move).** Root cause: in `RecordManualTimeAsync` STEP C attaches `existingSplit` (`CheckpointId==X`, `:1705`); STEP D then loads `nextSplit` (`FromCheckpointId==X`, `:1733`) and attaches it (`:1744`). Since rows have `CheckpointId==ToCheckpointId` and the START row is created with `FromCheckpointId==CheckpointId` (`RFIDImportService.cs:4724`), editing the start checkpoint makes STEP D re-return the SAME row already attached → duplicate-key throw (nondeterministic via unordered `FirstOrDefault`). **Audited the whole txn: this is the SOLE second-attach** — RN/Results/Participant each attach once; the only downstream recompute `CalculateResultRankingsAsync` (`:1750`) touches Results only via `BulkUpdateAsync` (tracker-bypass); the SplitTimes-attaching sibling `CalculateSplitTimeRankingsAsync` is NOT called from this path (only `:237`). `SaveChanges` alone wouldn't fix it (EF keeps rows tracked; the filter still matches). Fix (dedupe-by-identity, invariant option c): STEP D query excludes the edited row (`s.CheckpointId != decryptedCheckpointId`) + guard `nextSplit.Id != existingSplit?.Id`. No `AsNoTracking` added to a read-then-write path.

**ISSUE 2 — manual editor: show DATE + correct IST↔UTC round-trip (API + UI).** Two bugs: (a) manual save set `ReadNormalized.ChipTime = DateTime.UtcNow` (`:1608/:1620`) — the edit moment, not the crossing — so the table (which displays `ChipTime` UTC→event-TZ at `LoadCheckpointTimesAsync:1008`) showed garbage; (b) UI sent `finishTimeMs`=ms-from-midnight and the API treated it as elapsed-from-gun with no TZ conversion. Fix makes manual entry symmetric with automatic reads, keyed on `Event.TimeZone` (same source as display & `RFIDImportService.ParseSqliteFileAsync:1701`):
- **API:** `ManualTimeRequest` — added `string? CrossingLocalDateTime` (event-local wall clock, no offset), made `FinishTimeMs` nullable/optional (deprecated fallback). `IResultsService`/controller updated. `RecordManualTimeAsync` now loads `Event.TimeZone` (try/catch IST fallback), computes `crossingUtc` (local→UTC via event TZ) + `chipTimeMs = crossingUtc − raceStartUtc`, and stores `ChipTime = crossingUtc` at both RN sites. Legacy `finishTimeMs` paths retained (now also use `Event.TimeZone` instead of hardcoded IST). Display round-trips automatically once ChipTime is the real instant.
- **UI (`Runnatics.UI`):** `RFIDService.addManualTime` sends `crossingLocalDateTime` (no client-side TZ math). `ParticipantDetail.tsx` editor → `type="datetime-local"` (`step:1`), `handleStartEdit` defaults via `toLocalDateTimeInput(participant.startTime, currentTime)` (race-start local date + existing time-of-day; admin adjusts).

**Files:** API — `ResultsService.cs` (3 issues), `ManualTimeRequest.cs`, `IResultsService.cs`, `RFIDController.cs`. UI — `RFIDService.ts`, `ParticipantDetail.tsx`. **Builds:** Services ✅, full solution ✅ 0 errors, UI `npm run build` ✅. **Tests:** Services ✅ 19/19 (`DOTNET_ROLL_FORWARD=LatestMajor`). **No SQL / no schema change.**

**Prod verify (after deploy):** (1) 21.1K participant detail → child "-" rows gone, only named checkpoints show. (2) Edit a manual time with a date+time → table shows the entered IST time-of-day (not the edit moment); stored `ChipTime` UTC = entered IST − 5:30. (3) Edit the **Start** checkpoint manual time → no 500. **Note:** Race 47 `StartTime` 23:59 placeholder is the pre-existing day/gun-config quirk (see 2026-06-16 entry) — out of scope here; elapsed-from-gun depends on it being correct.

**Follow-up 2 (same session) — manual-time editor defaulted to the WRONG DAY → negative chipTime (Opus EXECUTE).** Prod test (deployed Issue-2 fix): editing Start showed `2026-05-09 05:51:31` and save failed `Calculated chip time -85049000ms is invalid`. Root cause in the Issue-2 UI: the datetime-local default took its DATE from `participant.startTime` (= `Race.StartTime`, the day-early `2026-05-09 23:59` placeholder) and its TIME from the split's IST display — two sources, and `Race.StartTime` serializes kind-less (`"2026-05-09T23:59:00"`, no `Z`) so `new Date()` parsed it as browser-local → locked to May 9. Math confirms: editor `2026-05-09T05:51:31` IST → UTC `2026-05-09 00:21:31` − DB StartTime `2026-05-09 23:59:00` = −85,049,000 ms (exact). The crossing's real date (May 10, ~06:00 IST) was never consulted — the UI had no field carrying it (`CheckpointTimeInfo.Time` is time-only). **Fix:** API adds `CheckpointTimeInfo.LocalDateTime` (event-local `yyyy-MM-ddTHH:mm:ss`, from the reading's ChipTime→IST already computed for `Time`; null if not crossed). UI editor pre-fills DATE+time from `checkpointTime.localDateTime` (no browser Date parsing); fallback borrows the crossing date from any sibling checkpoint + this row's time-of-day, else today; removed `participant.startTime` as the date source. The negative-chipTime guard stays as the safety net. Files: `CheckpointTimeInfo.cs`, `ResultsService.cs` (API); `CheckpointTime.ts`, `ParticipantDetail.tsx` (UI). Builds ✅ API+UI, tests ✅ 19/19. **Still pending — PART 2: consolidate to one "Save & Process Result" action** (auto per-participant recalc + race re-rank after a manual-time save, sequenced after commit on a clean context). NOT started — gated on user verifying the date round-trip first. **Reminder:** Race 47 StartTime is the day-early `23:59` placeholder (~31 min before the real `2026-05-10 00:30 UTC` gun) — chipTime is now positive but its magnitude stays offset until that data is corrected (pre-existing, separate).

**Follow-up (same session) — "second operation started on this context" after the Issue-3 fix (Opus EXECUTE).** Once Issue 3 stopped the Start-checkpoint edit throwing at STEP D, execution reached the `computedStatus == Finished` branch (editing Start can complete the last missing mandatory distance) and surfaced a **pre-existing** DbContext concurrency bug: `_ = Task.Run(() => _raceNotificationService.NotifyRaceCompletionAsync(...))` (~`:1815`) ran on a background thread while `_raceNotificationService` shares THIS request's scoped `IUnitOfWork<RaceSyncDbContext>` (RaceNotificationService.cs:12) and immediately issues a DB read (`LoadParticipantAsync`). That raced the awaited `updatedResult`/`count` reads right after it on the same context → "A second operation was started on this context instance." (Also latent: the request context is disposed at end-of-request, so the fire-and-forget could hit a disposed context.) **Audit:** every other DB call in `RecordManualTimeAsync` (incl. the new TZ-resolution query, both ChipTime write sites, STEP D) is awaited and sequential; this `Task.Run` is the only overlap, and the only one in the whole file. **Fix:** inject `IServiceScopeFactory`; the fire-and-forget now creates its OWN scope, resolves a fresh `IRaceNotificationService` (own DbContext), and is wrapped in try/catch with logging. Removed the now-unused `_raceNotificationService` field/ctor param (resolved per-call from scope). Build ✅ 0 errors, full solution ✅, tests ✅ 19/19.

### 2026-06-16 — Race 49 (5 Km) blank splits / rank #0 for moved runners — DIAGNOSIS (Opus; data-fix handoff, no code change)

**Symptom:** moved/late-added runners in **RaceId 49** (event 30, title "5 Km") show blank Split Times + rank #0. Reported via encrypted participant `364YW9nVJ_mxs1DCS4bCFQ` (= **ParticipantId 25590, bib 1012, Deepika Dalal**) and encrypted race `dwNcr4cQCwLRDfTvAIWkYA` (= **RaceId 49**). UI "RFID Tag Readings: 17 total · 0 normalized · 14 unassigned".

**ID note:** prod `Encryption:Key` (Azure env var) ≠ local user-secrets key, so prod encrypted IDs do NOT decrypt locally (AES-CBC, key=SHA256(key), iv=SHA256(key+"_iv")[..16], base64url, in `EncryptionService.cs`). Resolved the participant/race by DB fingerprint instead. DB read access: `sqlcmd` + connection string from user-secrets `add88346-…`.

**Root cause = day-early `Races.StartTime` for RaceId 49** (`2026-05-08 23:59:00`; real gun was night of `2026-05-09`, earliest start-mat read `2026-05-09 23:58:50.798`). The Phase 2 guard `RFIDImportService.cs:1983` (`daysDiff = |StartTime − earliestReading| > 1` ⇒ abort whole Phase 2) is evaluated over the **post-exclusion `rawReadings`** set (excludes already-normalized via event-wide `existingNormalizedReadIds`, line 1862/1957). Original full run normalized 258/318 because the field's earliest read was 0.99988 d (10 s under). The **6** movers/late-adds processed in a later incremental run (bibs 2295,2283,1069,**1012 Deepika**,1173 Hansraj=prev mover,1256) have earliest remaining read `2026-05-09 23:59:57.806` = **1.00067 d — 58 s over** ⇒ guard aborts ⇒ 0 ReadNormalized ⇒ blank splits, Registered-only.

**Ruled out:** (X) incomplete assignment — the 14 "unassigned" are correct: dedup losers (rapid repeats at one mat) + off-course-device reads (`809d`/`ebed` are NOT race-49 checkpoints; race 49 loops Start=Finish on mat `ebf3`, 2.5 Km on `ebeb`). The 3 reads on race-49 devices assigned correctly (Start 321, 2.5Km 322, Finish 324). (Contamination) — a `2026-01-25` read I surfaced was a **false alarm from my own query** that joined reads by EPC WITHOUT Phase 2's batch/event scope; the faithful reproduction (batch `EventId=30`, `RaceId=49|NULL`) shows 34 reads, 0 outside May. Phase 2 IS event/batch-scoped, so cross-event reused-chip reads never enter the set.

**Fix (data + reprocess; handed to user for manual run — no repo code change):**
1. `UPDATE Races SET StartTime='2026-05-09 23:59:00' WHERE Id=49 AND StartTime='2026-05-08 23:59:00';` (off-by-one-day correction; ≤a few min around 23:58 all work — pre-gun start crossings handled by BUG-27 clamp; negative-GunTime gate `:2470` only checks FINISH reads, so a ~10 s pre-gun start read is safe).
2. Full force-reprocess: `POST /api/RFID/{encEvent30}/dwNcr4cQCwLRDfTvAIWkYA/process-all?forceReprocess=true` (Clear+rebuild) — rebuilds all 318 incl. the 258's currently ~24 h-inflated GunTimes (NetTimes were always correct — from each runner's own start-mat crossing).
3. Verify: 6 runners (25555,25543,25647,25590,25751,26299) get NormCount>0 + ranked; Deepika finish GunTime ~5.55M ms (~1h32m) not ~92M ms (~25h).

**⚠️ Latent (NOT fixed, user deferred):** the `daysDiff` guard is **order-dependent** — same data passes on a full run but fails on incremental reprocess of movers (10 s-under vs 58 s-over margins). Correcting StartTime resolves this incident; the fragility (guard comparing post-exclusion MIN, fires inconsistently when StartTime drifts within a day) remains for any future race. Same wafer-thin margin flagged in the 2026-06-14 entry below. Consider comparing against the race's overall earliest reading or a non-fatal per-participant clamp — separate pass.

### 2026-06-14 — Process-result "Phase 2 failed: Deduplication error" — surfaced the real reason (Opus; diagnosis + wrapper hygiene)

**Symptom:** `process-result` on the 5K (RaceId 49) for a moved runner (ParticipantId 25751 — bib 1173 is REUSED across 18 races, so key on ParticipantId, see [[project_bib_not_unique]]) returned 400 `"Phase 2 failed: Deduplication error"`.

**Diagnosis (DB queries against 25751 + schema from EF configs — RawRFIDReadings has NO EventId/ParticipantId; link is `Epc → Chips.EPC → ChipAssignments.ChipId{ParticipantId}`):**
- The move's Save step (commit `a11e94d`) **worked perfectly**: 7 reads reset to `Pending`, 0 checkpoint assignments, 0 leftover ReadNormalized. Vindicated.
- Killed every code-line hypothesis: `:1898` ToDictionary collision = 0 **DB-GLOBAL** (the query must be global — `activeAssignments` at `:1888` has NO event/race filter); `Race.StartTime` not null; no leftover ReadNormalized.
- `"Phase 2 failed: Deduplication error"` has ONE source — the wrapper `RFIDImportService.cs:1558`, fired on `dedupeResponse.Status=="Failed"` **regardless of exception**. The catch at `:2293` logs `"Error during deduplication and normalization"` (NOT `"Error during deduplication:"` — that string is only the un-logged `ErrorMessage` property; a search for it gives a false "no log").
- `DeduplicateAndNormalizeAsync` controlled-fail branches: `:1764` race-null, `:1776` StartTime-null, `:1978` `daysDiff>1`, `:1988` `minutesDiff<-60`. Per data, **none fire**: Race 49 StartTime `2026-05-08 23:59`, earliest read `2026-05-09 23:56` → `daysDiff=0.9982` (misses `>1` by 2.6 min), `minutesDiff=+1437`. So it's a **real throw** (catch `:2293`) the wrong-string log search missed — OR a stale observation.

**Real config bug found (fix outside code):** `Race 49.StartTime` is **a day early** (reads are ~1 day after) → produces ~24h GunTimes. The `:1978` guard was meant to catch exactly this but misses by 2.6 min. Correct the StartTime to the real gun (just before `2026-05-09 23:56`).

**Change (`RFIDImportService.cs`, `ProcessCompleteWorkflowAsync`):** the 4 phase wrappers (`:1507` P1, `:1558` P2, `:1585` P2.5, `:1609` P3) now **surface the captured `ErrorMessage`** (old generic string as fallback) instead of hiding it. The 400 body now states the real reason (e.g. the actual exception, or `"Race.StartTime … is more than 1 hour AFTER the earliest reading"`). Build ✅ 0 errors, tests ✅ 19/19.

**Next:** deploy + fix Race 49 StartTime + re-run process-result → it succeeds or the 400 now names the exact cause.

### 2026-06-14 — Auto-process on participant-edit Save (UI repo `Runnatics.Ui`, commit `3b13fcc`) — Opus EXECUTE

**Why:** the race-move now rebuilds correctly on Process Result, so the manual "go to the other race and click Process" step is removed. The edit dialog (`EditParticipant.tsx`) detects what changed and chains the right follow-up sequentially after Save commits.

**Case mapping (race change takes precedence — full process covers a category change too):**
- **Race changed →** `POST participants/{eventId}/{targetRaceId}/{participantId}/process-result` = full `ProcessCompleteWorkflowAsync` rebuild on the TARGET race. Button "Save & Process Result".
- **AgeCategory changed (same race) →** `PUT participants/{eventId}/{raceId}/{participantId}/race-category` = cheap whole-race re-rank (`ChangeParticipantCategoryAsync` → `CalculateResultRankingsAsync`; re-ranks EVERY category bucket, so the bucket left AND joined both correct; NO Phase 1/1.5/2, readings untouched). Button "Save & Re-rank". Also closes the latent stale-category-rank gap (edit-form same-race path never re-ranked before).
- **Scalar only →** plain Save. Button "Update Participant".

**Wiring:** strictly sequential (await Save commit → THEN follow-up; never parallel). Save fails → no follow-up, show save error. Save ok but follow-up fails → recoverable `warning` Alert + **Retry** (re-fires ONLY the follow-up; runner is saved + re-processable), not a red error. Dynamic button label + combined progress (Saving… → Processing…/Re-ranking…).

**No API change** — `process-result` already runs the full workflow (commit `a11e94d`); the `race-category` endpoint already existed and re-ranks the whole race.

**UI plumbing (same bug class as BUG-B):** `ServiceUrls.changeRaceCategory` had the WRONG route (`participants/${participantId}/race-category` — missing `eventId/raceId`); corrected to `participants/${eventId}/${raceId}/${participantId}/race-category` (verified against the `[HttpPut("{eventId}/{raceId}/{participantId}/race-category")]` attribute). Added `ParticipantService.changeRaceCategory(eventId, raceId, participantId, ageCategory)` (`PUT` body `{ ageCategory }`).

**Build:** UI ✅ 0 errors (`vite build`), type-check clean. **Files:** `models/ServiceUrls.ts`, `services/ParticipantService.ts`, `pages/admin/participants/EditParticipant.tsx`.

### 2026-06-14 — Race-move SHARED-COURSE correction: delete-to-DNS → re-project from raw (Opus EXECUTE; supersedes the consolidation refactor's DNS model)

**Why:** the consolidation refactor (below) assumed a cross-distance move leaves the runner with "no target detections → DNS". WRONG for this **shared-course** event: all distances start together and run the same physical route to their split point, crossing the **same physical mats**. A physical crossing is stored **once** and is race-independent; which race it projects into is decided at processing time via EPC→Participant→**current** `RaceId`. So a 21.1K runner moved to the 5K HAS real crossings at the 5K's mats → must get a **real** result, never a forced DNS. Correct model: a move is **"re-register + reprocess from raw against the new race"**, reusing the proven pipeline — not a bespoke move-time mapper, and not delete-to-DNS.

**Data-model facts CORRECTED (supersede the entry below):**
- **`ReadRaw` is a DEAD table** — nothing writes it (only the config registration). The live raw layer is **`RawRFIDReading`** (`BatchId`,`DeviceId`,`Epc`,`TimestampMs`,`ReadTimeUtc`,`ProcessResult`). `ReadNormalized.RawReadId` is declared FK→`ReadRaw` but is populated with `RawRFIDReading.Id` (`RFIDImportService.cs:1959,2189`). The move must **retain `RawRFIDReading`** (the earlier "retain ReadRaw audit trail" gate checked the wrong table).
- **Event-level uploads** (`UploadBatch.RaceId = NULL`, the confirmed mode here) are visible to every race's processing (`b.RaceId == target || b.RaceId == null`). Re-normalizing a moved runner against the target picks up their shared-mat reads automatically once `Participant.RaceId = target`.
- **`ChipAssignment`** PK `(EventId,ParticipantId,ChipId)` — participant-scoped, no RaceId → EPC→Participant resolves unchanged after the move.

**The three gates a cross-race move must clear (and why Process Result = the full workflow):**
- (a) EPC→Participant by RaceId — auto once `Participant.RaceId=target`. ✅
- (b) Phase 2 SKIPS already-normalized reads (`existingNormalizedReadIds`, event-wide) → must **hard-delete the participant's `ReadNormalized`** so their reads are re-eligible.
- (c) Phase 1 SKIPS reads with an active `ReadingCheckpointAssignment` and Phase 2's `ToDictionary(ReadingId)` collides on a stale one → must **hard-delete the participant's `ReadingCheckpointAssignment`**.
- **Simple-race trap:** Phase 1 loads `ProcessResult=="Pending"` only, and Phase 1.5 (shared-device assigner) **early-returns for simple linear races** (no device shared across ≥2 checkpoints). After a race is timed the reads are `"Success"`, so a move into a SIMPLE target race would re-assign **nothing** → false DNS. Fix: **reset the participant's `RawRFIDReading.ProcessResult = "Pending"`** at Save so Phase 1 reloads & re-assigns them.

**⚠️ DEPENDENCY (make visible):** the reset-to-Pending works because **EVENT-LEVEL uploads keep their batches perpetually `"uploaded"`** (set at parse time `RFIDImportService.cs:763`; Phase 1 marks ONLY race-level batches `"completed"`, never event-level), so Phase 1's batch gate always includes them and a per-reading reset suffices (no batch flip). **If a future event uses RACE-LEVEL uploads** (those batches DO get `"completed"`, and `ClearProcessedData` never resets event-level batches), this reset-to-Pending path must be revisited.

**Changes:**
- **`ParticipantImportService.MoveParticipantToRaceAsync`** (Save, one transaction, all participant-scoped via EPC→event reads): (1) move registration on the same row; (2) **reset this participant's `RawRFIDReading`→Pending** (clear `ProcessedAt`/`AssignmentMethod`/`Notes`, `BulkUpdateAsync`); (2b) **hard-delete their `ReadingCheckpointAssignment`** (`BulkDeleteAsync`); (3) **hard-delete their `Results`/`SplitTimes`/`ReadNormalized`**; retain `RawRFIDReading`; (5) re-rank **source** via `ReRankRaceAsync` (bulk, no `Include`). EPC scope starts from this participant's `ChipAssignment` rows → no other participant's reads touched.
- **`ResultsService.ProcessParticipantResultAsync`** now injects **`IRFIDImportService`** (cycle-free — RFID svc has no `IResultsService` dep) and runs **`ProcessCompleteWorkflowAsync(event, race)`** (Phase 1→1.5→2→2.5→3) before the per-participant status confirm. This rebuilds the moved runner's timing from raw; idempotent for everyone else (skip-guards leave still-`Success`/still-normalized runners untouched). Runs on a **fresh request scope** (process-result is a separate HTTP request from the edit/save), so no collision with the move transaction under the global NoTracking default. Workflow `Status=="Failed"` → `ErrorMessage` + return false (recoverable: admin re-hits Process Result; raw retained).

**Outcomes (acceptance matrix — user walks in prod):** 21.1K→5K full-course → real 5K finish; 5K→21.1K ran-only-5K → DNF (missing later mandatory); 5K→21.1K actually-ran-21.1K → full finish; true no-show → DNS (natural); **SIMPLE linear target → real result, NOT DNS** (the reset-to-Pending case).

**Build:** ✅ 0 errors (solution). **Tests:** Services ✅ 19/19. A move integration test was **not feasible** in the current harness (pure-algorithm tests only; `BulkUpdate/BulkDelete` need a real relational provider — EF in-memory can't exercise them). **Supersedes** the delete-to-DNS model and the "retain ReadRaw" gate in the entry directly below.

### 2026-06-14 — Race-move CONSOLIDATION REFACTOR (the real fix; replaces the 3-patch saga) — Opus EXECUTE

**Why:** three patches to `MigrateParticipantToRaceAsync` each exposed the next layer (tracking 500 → SaveChanges flush → `DbUpdateConcurrencyException` "expected 1 row, affected 0"). Root model was wrong: the method **reassigned derived/normalized timing in place across races** (`UPDATE Results.RaceId`, `UPDATE ReadNormalized.CheckpointId+ParticipantId`, soft-delete SplitTimes). A 21.1K finisher's 9 detections / 8 splits live at 5/10/15/21 km mats that **don't exist on a 5K** — carrying them is garbage AND the phantom-PK 0-rows source. No concurrency token exists anywhere (verified), so 0-rows = PK-absent at write time, not a token mismatch. `BulkUpdate` would have HIDDEN the 0-rows and persisted corrupt data — explicitly rejected.

**Data-model facts established:** `ReadNormalized` is **race-bound** (no `RaceId` column; bound via `CheckpointId`→race-specific `Checkpoint` + race-relative Gun/Net times; `RawReadId`→`ReadRaw`). `ReadRaw` (EPC/reader/timestamp) is the **immutable physical-detection audit trail**, NOT participant-keyed; `ReadNormalized.RawReadId→ReadRaw` is `OnDelete:SetNull`, so deleting `ReadNormalized` never touches `ReadRaw` (confirmed before deleting). `Results↔Participant` is 1:1 with a UNIQUE `Results.ParticipantId` index.

**Refactor (`ParticipantImportService.cs` only):**
- `MigrateParticipantToRaceAsync` → **`MoveParticipantToRaceAsync`** (runtime move, not an EF migration; old name violated no-migrations convention + misled debugging). Both callers renamed (`EditParticipant`, `UpdateParticipantExtendedAsync`).
- **SAVE (one transaction):** move registration on the **same Participant row** (`RaceId=target`, `Status="Registered"`, audit); **HARD-DELETE** this participant's `Results`/`SplitTimes`/`ReadNormalized` via `BulkDeleteAsync` (retain `ReadRaw`); `ChipAssignment` untouched (same row); re-rank **source race only**. No cross-race reassignment, no new participant row, **no inline target reprocess**.
- `RecalculateRaceRanksAsync` → **`ReRankRaceAsync`**: dropped `.Include(Participant)` (no graph cascade), projects gender/category via dict, writes via `BulkUpdateAsync`.
- Removed now-unused `IServiceScopeFactory` injection + DI using (the `afe3cbe` inline reprocess is gone).
- **PROCESS RESULT unchanged** — owns the target rebuild (fresh context). Cross-distance move → participant has no target detections → DNS (correct) until manual time.

**Why the failure class is gone:** only ONE tracked entity (participant) per transaction (everything else bulk) → no NoTracking double-attach; derived rows deleted not re-read-then-written → no phantom-PK 0-rows; derived data never carried across layouts → no corrupt 21km-on-5K data.

**Build:** ✅ 0 errors. **Tests:** Services ✅ 19/19.
**Prod verify (bib 2148, 21.1K→5K):** Save → Registered/DNS in 5K, source 21.1K re-ranked without them, no 500, no 0-rows, `ReadRaw` retained.
**Booked follow-up:** same-distance move should re-normalize from retained `ReadRaw` instead of delete-to-DNS.
**Supersedes:** the BUG A `afe3cbe` (fresh-scope) + `d43d59b` (SaveChanges flush) patches — both targeted symptoms of this wrong model.

### 2026-06-14 — BUG B (process-result 404) — DONE (UI repo)

- **Symptom:** `POST /api/Results/{eventId}/{raceId}/participant/{participantId}/process-result` → 404 from the participant detail page.
- **Root cause:** the endpoint lives on `ParticipantsController` (`api/Participants/{eventId}/{raceId}/{participantId}/process-result`, `ParticipantsController.cs:609`), NOT `ResultsController`. The FE URL had the wrong controller prefix (`Results`) AND an extra `participant/` path segment → no route match → 404. Backend endpoint + `ProcessParticipantResultAsync` were correct; no API change needed.
- **Fix (UI repo `C:\Projects\Runnatics.UI\Runnatics.Ui`, commit `c1b0cc8`):** `src/main/src/models/ServiceUrls.ts:47` `processParticipantResult` → `` `participants/${eventId}/${raceId}/${participantId}/process-result` `` (matches the sibling `getParticipantDetections` pattern). `npm run build` (Vite) ✅.

### 🏛️ INVARIANT — global `QueryTrackingBehavior.NoTracking` (Program.cs:100)

`AddDbContextPool<RaceSyncDbContext>` sets `options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)`. **Consequence:** every `GetQuery` (raw `_dbSet`, no `.AsTracking()`) returns a **FRESH untracked instance with NO identity resolution**. Re-querying the same row in one unit of work yields a DIFFERENT object with the same key.
- `UpdateAsync`/`UpdateRangeAsync` call `_dbSet.Update(...)` which **attaches**. If the SAME Id is loaded twice and both are attached, EF throws "another instance with the same key value for {'Id'} is already being tracked".
- **Rule:** when two write paths can touch the same Id within one transaction, either (a) `SaveChangesAsync()` between them (flush so a later read sees the new state / no longer matches), or (b) use bulk ops (`BulkUpdateAsync` bypasses the tracker), or (c) dedupe by Id before attaching. Do NOT assume identity resolution protects you — it does NOT in this codebase. (Same family as the enum-vs-DB-string bugs: an assumption that's silently false here.)

### 2026-06-14 — BUG A (edit-participant 500: EF Results double-attach on race-move) — Opus EXECUTE (CORRECTED root cause + real fix)

**Symptom:** `PUT /api/participants/{id}/edit-participant` → 500 when moving a runner to a different race. Error: "The instance of entity type 'Results' cannot be tracked because another instance with the same key value for {'Id'} is already being tracked." **Deterministic, FINISHERS ONLY** (repro: bib 2148, a 21.1K finisher, → 5K).

**REAL root cause (corrected — see NoTracking invariant above):** inside `MigrateParticipantToRaceAsync`'s transaction, the moved participant's `Results` row is attached TWICE:
1. `:2394` `oldResults` loaded NoTracking (fresh instance, Id=N) → reassigned → `:2409` `UpdateRangeAsync` **attaches** Id=N.
2. No SaveChanges yet → DB row still shows SOURCE race. `:2481` `RecalculateRaceRanksAsync(sourceRaceId)` re-queries the source race filtered `Status=="Finished"` (`:2504`, NoTracking → a SECOND fresh instance, same Id=N) → `:2545` `UpdateRangeAsync` tries to attach Id=N → **THROW**.
- Finisher-only because the rank query filters `Status=="Finished"`; a DNF/DNS row isn't returned, so it's attached only once.

**Why the earlier `afe3cbe` fresh-scope fix did NOT fix it:** the throw is INSIDE the migration transaction, BEFORE the post-commit reprocess that `afe3cbe` isolated. And that reprocess (`CalculateRaceResultsAsync`) writes only via `BulkInsert/BulkUpdate` which BYPASS the tracker — it was never the thrower. (The original research mis-assumed identity resolution; the global NoTracking default inverts that. `afe3cbe` is harmless/defensible isolation but targeted an already-safe path.)

**FIX (approved):** added `await _repository.SaveChangesAsync();` after the reassignment block (after chip-assignment UpdateRange, `~2477`) and BEFORE `RecalculateRaceRanksAsync` (`~2481`), still INSIDE `ExecuteInTransactionAsync` (flush, not commit → atomicity preserved; precedent at `:2387`). Once `RaceId=target` is flushed, the source-race rank query no longer returns the row → no second instance. **Bonus correctness fix:** the moved finisher is no longer re-ranked back into the race it's leaving.
- Did NOT add `AsNoTracking` anywhere (already the global default; the read-then-write paths rely on the attach).
- **Source race** re-ranks WITHOUT the moved runner (gap closed). **Target race** is re-ranked by the awaited post-commit `CalculateRaceResultsAsync(targetRace)` (fresh scope, sets Overall/Gender/Category ranks) — no stale/missing rank after Save.

**Duplicate-Results guard (already done in `afe3cbe`, confirmed present):** `RFIDImportService.CalculateRaceResultsAsync:2549-2561` dedupes `existingResults` by ParticipantId (keep highest Id + warn) instead of `ToDictionaryAsync` → no duplicate-key `ArgumentException` in the target-race reprocess.

**Build:** `dotnet build` ✅ 0 errors. **Tests:** Services ✅ 19/19 (`DOTNET_ROLL_FORWARD=LatestMajor` for net8.0).
**Files modified this pass:** `Runnatics.Services/ParticipantImportService.cs` (SaveChanges before source re-rank). (`afe3cbe` already shipped the fresh-scope isolation + dedupe guard.)
**Prod verify (after deploy):** move bib 2148 (21.1K → 5K) via the **Race** dropdown → confirm no 500, source (21.1K) re-ranked without them, target (5K) ranks them in.

**Problem 2 (UI, "process-then-save doesn't move runner") — ✅ RESOLVED, NOT a bug (user-confirmed 2026-06-14).** User verified in the network tab: changing the **Race** dropdown DOES send the target 5K id in the PUT `raceId`. The earlier payload (`category:"18 to 30"` changed, `raceId` unchanged) was the admin changing the **Category** dropdown — which relabels the age bracket within the SAME race and never triggers a move. `ParticipantDetail.tsx` race-move binding is correct (`handleRaceIdChange → confirm → setEditRaceId → raceId:editRaceId`). No UI change needed. (UX note for the future refactor: Category vs Race dropdowns are easy to confuse — consider clearer labeling/separation.)

**📌 BOOKED (separate session) — race-move/category-change consolidation refactor.** This path has now produced 5 distinct bugs. Target model: on **Save** = move registration + invalidate derived data; on **Process Result** = recompute splits/results + re-rank BOTH races; derived data always rebuilt, never carried across races. Settle first: is `ReadNormalized` keyed by RaceId or by EPC+Event (decides whether "move the readings" is real work or a reprocess no-op). Do NOT fold into a bugfix — dedicated pass.

(BUG B — process-result 404 — ✅ DONE, UI repo `c1b0cc8`; see entry above.)

---

### 2026-06-14 — Gender canonicalization (NormalizeGenderForWrite + filter + rank bugs) — Sonnet EXECUTE

**Scope:** Three tightly-scoped fixes in `ParticipantImportService.cs` only.

**PART 1 — `NormalizeGenderForWrite` helper + 4 write sites:**
- Added private static `NormalizeGenderForWrite(string raw)` at line ~1445 (after `MapRaceStatusToDbString`).
  Rule: trim+ToUpperInvariant → "M"/"MALE"→"M", "F"/"FEMALE"→"F", anything else → `raw.Trim()` (pass through).
- Applied at all 4 write sites (lines 315, 773, 838, 1352); the `?? "Unknown"` fallback for null/whitespace is unchanged — only non-empty values are now normalized.
- EF Core `GenderNormalizer` value converter (`ParticipantConfiguration.cs`) left untouched; it remains the safety-net for any path that bypasses these sites.

**PART 2 — `MapGenderToDbString` helper + 2 filter sites:**
- Added private static `MapGenderToDbString(Gender gender)` mirroring `MapRaceStatusToDbString` pattern.
  `Gender.Male→"M"`, `Gender.Female→"F"`, `_→null`.
- Applied at both filter sites:
  - Line ~423: paginated search `if (request.Gender.HasValue)` block (guard `if (genderString != null)` added).
  - Line ~1420: `BuildSearchExpression` predicate (direct substitution).
- **Root cause was identical to BUG-2 status filter:** `Gender enum.ToString()` → `"Male"` ≠ DB `"M"` → filter never matched any rows.

**PART 3 — Fix `RecalculateRaceRanksAsync` gender rank:**
- Line ~2506: changed `new[]{"Male","Female","Others"}` to `new[]{"M","F"}` to match canonical DB values.
- `ResultsService` (lines 1312, 1362) already used `"M"/"F"` correctly; this aligns the only remaining divergent site.

**Pattern note (logged for future):** enum `.ToString()` vs canonical DB string has now bitten us in BUG-2 (status) and here (gender, 3 sites). Any future enum-backed string column comparison MUST use an explicit mapping helper, never `.ToString()`.

**SQL for data cleanup (provide to user for manual run):**
```sql
-- INSPECT first — see what non-canonical values remain
SELECT RaceId, Gender, COUNT(*) AS Cnt
FROM Participants
WHERE IsActive=1 AND IsDeleted=0
  AND Gender NOT IN ('M','F','Unknown')
  AND Gender IS NOT NULL
GROUP BY RaceId, Gender;

-- Fix casing/spelled-out only (Unknown left untouched)
UPDATE Participants SET Gender='M'
WHERE IsActive=1 AND IsDeleted=0
  AND UPPER(LTRIM(RTRIM(Gender))) IN ('M','MALE');
UPDATE Participants SET Gender='F'
WHERE IsActive=1 AND IsDeleted=0
  AND UPPER(LTRIM(RTRIM(Gender))) IN ('F','FEMALE');
```

**Build:** `dotnet build` ✅ 0 errors (pre-existing warnings only).
**Tests:** .NET 8 runtime not installed on this machine (only .NET 10) — tests target net8.0 and cannot run. Set `$env:DOTNET_ROLL_FORWARD='LatestMajor'` or install .NET 8 runtime to run locally.
**Files modified:** `Runnatics.Services/ParticipantImportService.cs` only.

---

### 2026-06-13 — BUG-1 (gender reset on save) + BUG-2 (status filter broken) — admin participant screens — Sonnet EXECUTE

- **BUG-1 — Gender lost on ParticipantDetail.tsx inline save:**
  - Root cause: `ParticipantDetail.tsx` edit form had no `editGender` state; `handleSaveEdit` sent no `gender` field → backend coerced missing/null gender to `"Unknown"`. The `EditParticipant.tsx` modal (from ViewParticipants list) was already correct.
  - Fix (`ParticipantDetail.tsx`, UI repo): added `toGenderValue()` helper (same as `EditParticipant.tsx`); added `const [editGender, setEditGender] = useState(')`; populated in populate-useEffect via `setEditGender(toGenderValue(participant.gender || '))`; added Gender Select (M/F/Other) to edit form; added `gender: editGender || undefined` to `handleSaveEdit` payload.
  - Note: full-name-in-firstName / empty-lastName data issue flagged per user instruction, not fixed (data-import problem).

- **BUG-2 — Status filter returns nothing for "Completed" and "No Show":**
  - Root cause: `ParticipantImportService` at two sites called `.ToString()` on `RaceStatus` enum → `Completed→"Completed"` ≠ DB `"Finished"`, `NoShow→"NoShow"` ≠ DB `"DNS"`. Registered and DNF worked by coincidence (enum names match DB strings).
  - Fix (`ParticipantImportService.cs`): added private static `MapRaceStatusToDbString(RaceStatus)` (`Completed→"Finished"`, `NoShow→"DNS"`, others→name); applied at both filter sites — paginated-search `if (request.Status.HasValue)` block (~line 414) and `BuildSearchExpression` predicate (~line 1418). No enum rename, no DTO change, no SQL.
  - Fix (`ViewParticipants.tsx`): renamed "No Show" MenuItem label to "DNS".
- **Builds:** `dotnet build` ✅ 0 errors · `npm run build` ✅ built in ~20s.

### 2026-06-13 — Public Split Details: speed bug + start-row 00:00:00 (BUG-25 display, page-scoped) — Opus EXECUTE

- **Symptom (Bib 2262, RaceId 47):** public Split Details page (`racetik.com/p/{id}` → "Split Details" tab) showed impossible running speeds (30.86 / 43.49 / 64 / 79 / 86 km/h) and a Start row of 00:00:33 — even though the backend `SplitTimes` data was correct (post Clear-gate rebuild).
- **Root cause was BACKEND, not UI** (the brief assumed UI repo): `ParticipantDetailPage.tsx` / `getParticipantDetail` (`Runnatics.UI .../src/api/publicApi.ts`) do **zero** math — they render `split.speed/splitTime/raceTime/splitDist` verbatim. The computation is in `PublicResultsService.GetPublicParticipantDetailAsync`. The speed line divided the **cumulative** `ToCheckpoint.DistanceFromStart` by the **per-segment** `SegmentTime` (`st.Distance` is never populated by the pipeline, so it always fell back to cumulative distance). `cumulativeDist ÷ segmentTime` reproduced every reported number to the decimal.
- **Sweep (user-requested) — the buggy `cumulativeDistance ÷ SegmentTime` formula existed in EXACTLY ONE place.** `ResultsService.RecordManualTimeAsync` (`~1582`) already computed segment speed correctly; `PublicResultsService` avg-pace (`~544/567`) and `ResultsService` (`~167`) are legitimate cumulative-time ÷ cumulative-distance avg pace. 🟡 Noted out-of-scope: `ResultsService.CalculateResultsAsync:167` writes `SplitTimes.Pace` as a *cumulative* avg pace while the manual path writes a *segment* pace — latent pace-semantics inconsistency, tracked separately.
- **Fix (`PublicResultsService.cs`, `GetPublicParticipantDetailAsync` split projection ~584-606; backend-only, no UI/DTO/SQL change):**
  1. **Speed** = `segDist / (SegmentTime / 3_600_000)` where `segDist = thisDist − prevDist` (previous row's `DistanceFromStart`; splits already ordered by distance). Guarded `segDist > 0` and `idx > 0` for the prev-row access.
  2. **Start row** (`DistanceFromStart == 0`) → `SplitTime` = `RaceTime` = `"00:00:00"`, `Speed = null` (renders "—"). Keyed on **distance, not row index**, so a finisher who missed the start mat (first row at >0 km) is NOT wrongly zeroed.
  3. All other rows keep `SegmentTime`/`SplitTimeMs` unchanged.
- **Trace (Bib 2262):** speeds now 17.0 / 16.2 / 13.1 / 18.3 / 15.3 / 15.0 / 14.8 km/h (all ~13–18); Start row 00:00:00 / 00:00:00 / —.
- **Build:** `Runnatics.Services` ✅ 0 errors. **Commit** `f5f148b` (pushed to master). No deploy of UI needed.
- **Scope note:** this is the **page-scoped** BUG-25 display piece (start row = 0). Other BUG-25 surfaces (admin participant detail, leaderboard split view, Excel export, grouped drill-down) remain tracked under BUG-25 (PENDING).

### 2026-06-11 — Post-deploy regression round (ISSUE-1/2/3)

- **ISSUE-3 (gender dropdown resets to "Unknown") — FIXED (UI only).**
  - Root cause: two compounding bugs. (1) `ViewParticipants.normalizeGender` hands the edit dialog **lowercase** `"male"/"female"/"other"`, but `EditParticipant.tsx` Select options were capitalized `"Male"/"Female"/"Other"` → MUI Select rendered empty on load (no matching option). (2) On save, `gender: formData.gender?.trim() || undefined` — when the value was empty/legacy the `gender` key was **omitted** from the request, and backend `EditParticipant` (`ParticipantImportService.cs:837`) coerces a null/blank gender to the literal `"Unknown"`.
  - Fix (`EditParticipant.tsx`): Select option values changed to `value="M"/"F"/"Other"`; added module-level `toGenderValue()` helper that case-insensitively maps `m/male→M`, `f/female→F`, `o/other→Other`, anything else passes through — applied to the form's initial gender (`participant.gender`). Save still sends the raw value; the EF `GenderNormalizer` ValueConverter canonicalizes `M`/`F` on write.
  - Verify: `npm run build` ✅, `npx tsc --noEmit` ✅ exit 0.

- **ISSUE-2 (race move only moves participant data, not timing) — FIXED (backend only).**
  - Root cause: NOT a deploy/trigger bug. The BUG-06 timing migration lived only in `UpdateParticipantExtendedAsync`, but the participant edit form saves through `EditParticipant` (`PUT /api/participants/{participantId}/edit-participant`), whose own divergent race-move branch moved only the participant row + ChipAssignments (never Results/SplitTimes/ReadNormalized).
  - Fix (`ParticipantImportService.cs`): extracted the full BUG-06 migration into a shared private method `MigrateParticipantToRaceAsync(sourceParticipant, targetRaceId)` (returns the new participant, or null + ErrorMessage if target race invalid). Both `UpdateParticipantExtendedAsync` (passes `participant` + request target) and `EditParticipant` (passes `existingParticipant` + `decryptedRaceId` = target) now call it. No code duplication; the two previously-divergent race-move paths are unified.
  - Verify: `dotnet build` (Services + full solution) ✅ 0 errors. No public signatures changed; no SQL.

- **ISSUE-1 (7th GGHM 21KM splits not showing) — ROOT CAUSE CONFIRMED + FIXED (N-checkpoint assignment).**
  - **Root cause (user-confirmed via prod DB):** shared-device checkpoint assignment was hardcoded to 2 topologies — `count==1` (single/turnaround) and `count==2` (outbound/return). A device at 3+ sequential course locations (7th GGHM 21KM: Box-1 at 0/10.5/21.1km, Box-6 at 4 locations) matched no branch → `AssignAllCheckpoints` Case 4 drop → no `ReadingCheckpointAssignment` → excluded from normalization (`DeduplicateAndNormalizeAsync` requires an assignment) → zero `ReadNormalized`/`SplitTimes`. Only the one 2-location device (box04) produced normalized reads.
  - **Fix (per approved design doc `.claude/design/ISSUE-1-checkpoint-assignment-redesign.md`):**
    - `LoopRaceCheckpointAssigner.cs`: `SharedDeviceMapping` now holds an ordered `List<CheckpointSlot>` (by `DistanceFromStart`, tiebreak Name→Id) + `AssignmentMode` (`Sequential` = clamp extra passes to last checkpoint; `Cyclic` = pass % N) + `IndexForPass(pass)`. `ReadingInput.IsOutboundOverride (bool?)` → `PassIndexOverride (int?)` (0-based pass ordinal). `IdentifySharedDevices` filters `==2` → `>=2` (primary + child), takes a mode param; child devices inherit the parent's `SharedGroupKey` and identical distance-ordered slot lists. `ResolveOutboundReturn` → `OrderCheckpointsByDistance` (distance authoritative; equal-distance warning kept). `GenerateGroupKey` N-aware (`Start_10.5KM_Finish`). `AssignAllCheckpoints` Case 2: pass ordinal → `Checkpoints[IndexForPass(pass)]`; priority order **PassIndexOverride (production-dominant) → TurnaroundReference (generalized to first/last = legacy outbound/return for N=2) → ChronologicalOrder (rank-1 = pass ordinal)**.
    - `RFIDImportService.cs` (Phase 1.5 only, 4 edits): derive `assignmentMode` from `RaceSettings.HasLoops` (true→Cyclic, else Sequential); pass mode to `IdentifySharedDevices`; precompute `PassIndexOverride = pi` (was `IsOutboundOverride = pi==0`); start-bound collapse check uses `mapping.StartsAtZero`. Pass-gap/dedup loops, Phase 1 deferral, FIX #7 wipe-and-rebuild, Step 5 dedup, Phase 2 all untouched.
  - **Decisions (user-confirmed):** (1) PassIndex dominant, turnaround as retained fallback — matches production B behavior, no regression; (2) Cyclic implemented in assigner, but distinct-checkpoint-rows (`GenerateLoopCheckpoints`) is the SUPPORTED loop pattern — true cyclic reuse collapses laps downstream (Step 5 + Phase 2 keep one reading per participant+checkpoint; `ReadNormalized` has no lap column); (3) unit tests included.
  - **Tests (NEW `Runnatics\tests\Runnatics.Services.Tests\RFID\LoopRaceCheckpointAssignerTests.cs`):** 19 tests, all pass — `IndexForPass` (sequential clamp, cyclic wrap, negative/empty/single edges), `IdentifySharedDevices` (N=1/2/3/4, paired parent+child identical resolution, mode stamping, mixed counts), Topology B regression fixture (PassIndex/clamp/turnaround-fallback/chronological-fallback ≡ legacy outbound/return), Topology C (N=3 ordinal mapping + fewer-passes-than-checkpoints), Cyclic 2-lap wrap.
  - **Builds:** Services ✅ · full solution ✅ · `dotnet test` ✅ 19/19.
  - **Known limitations (documented in design doc §7):** L1 — true cyclic persistence needs lap-discriminated dedup keys + lap column on `ReadNormalized` (future, schema); L2 — a missed read on the SAME device group shifts later pass ordinals (location-blind hardware, monotonic validation can't catch; future expected-time-window mitigation); legacy upload-time path (`ProcessRFIDImportAsync` ~1108-1389, "LoopRaceSequence") left untouched — Phase 1.5 FIX #7 wipes/rebuilds its assignments.
  - **To verify the 7th GGHM fix in prod:** deploy, then re-trigger processing for the 21KM race (Phase 1.5 deletes + recreates all assignments) and confirm `ReadNormalized`/`SplitTimes` populate for all 6 devices. Note prior open item still stands: run the `IsMandatory` schema script if not yet applied (required by `CalculateRaceResultsAsync`).

### 2026-06-12 — BUG-26 (mandatory checkpoint evaluation per-DISTANCE, not per-checkpoint-ID)

- **Rule (user-approved):** mandatory distances = DISTINCT `DistanceFromStart` where any active checkpoint is `IsMandatory`; a distance is SATISFIED if the participant has active `ReadNormalized` at ANY checkpoint at that distance (flagged or not — covers unflagged sibling/child devices); `Finished` = ALL mandatory distances satisfied. Fallback when none flagged: max distance (now distance-based, accepts any device at that distance). Previously `Finished` required a detection at EVERY mandatory checkpoint **ID** → two mandatory devices at the same distance wrongly produced DNF.
- **Fixed in ALL FOUR sites** (research found 2 beyond the reported 2; user approved including them — one rule everywhere):
  1. `RFIDImportService.CalculateRaceResultsAsync` — distance-group build, widened `mandatoryDetections` query, per-distance classification; **finish gate widened** to all checkpoint IDs at the highest mandatory distance, with finish readings collapsed to ONE per participant (earliest GunTime) so ranking/result rows stay unique (status and time come from the same rule).
  2. `ResultsService.ComputeParticipantStatusAsync` — full rewrite to per-distance (loads Id+Distance+IsMandatory in one query).
  3. `ResultsService.RecordManualTimeAsync` — per-distance status after manual entry; now derives groups from the already-loaded `raceCheckpoints` (removed a redundant DB query); the in-memory `coveredCheckpointIds.Add(recordedCheckpoint)` still satisfies its own distance group.
  4. `ResultsService.CalculateResultsAsync` — per-distance finisher check (both occurrences) + finish time from a split at ANY checkpoint at the finish-gate distance (earliest `SplitTimeMs`; handles nullable `SplitTimes.CheckpointId`).
- **Verify:** `dotnet build` per site ✅ ×4 · full solution ✅ · assigner tests ✅ 19/19 · traced the 21.1km two-mandatory-device case through all four sites (sibling-only detection → Finished with time+rank; dual detection → one rank row).
- **⚠️ Env note:** .NET 8 runtime is no longer installed on this machine (only 10.0.8) — `dotnet test` needs `$env:DOTNET_ROLL_FORWARD='LatestMajor'` to run the net8.0 test project. Consider retargeting tests to net10.0 or installing the .NET 8 runtime.
- **✅ DNS start-gate also fixed (user-approved follow-up):** the DNS check in `CalculateRaceResultsAsync` now loads start readings from ALL checkpoint IDs at the start distance (`startGateCheckpointIds`) instead of the single `startCheckpoint.Id` — a participant read only by a sibling/child device at the start line now counts as "started" (DNF, not DNS). The DNS gate exists only in this site (ResultsService paths classify Finished/DNF only). Build ✅ · 19/19 tests ✅.
- **Prod verification (after deploy):** reprocess RaceId 47 (7th GGHM 21KM), then
  `SELECT Status, COUNT(*) FROM Results r JOIN Participants p ON r.ParticipantId=p.Id WHERE p.RaceId=47 AND r.IsActive=1 AND r.IsDeleted=0 GROUP BY Status;`
  Expected ≈ 142 Finished / 25 DNF / 51 DNS (Excel baseline).

### 2026-06-12 — BUG-27 Phase A (gun clamp on net-time start baseline) — Sonnet EXECUTE

- **Confirmed issue (prod, 7th GGHM RaceId 47):** Bibs 2242/2127 crossed the Start mat ~2m22s BEFORE the gun (`Race.StartTime`). The `EarlyStartCutOff` window (default 10 min, `RFIDImportService.cs:4061`) admits those pre-gun reads, but nothing clamped the baseline → `NetTime = finish − startMat` came out LARGER than `GunTime = finish − gun` → impossible Chip 1:40:45 > Gun 1:38:23.
- **Fix (1 site, `RFIDImportService.DeduplicateAndNormalizeAsync`, ~line 2062):** when building `participantStartTimes`, clamp the net baseline up to the gun: `clampedStart = (raceStartTime.HasValue && raceStartTime > startMat) ? gun : startMat`. Guarded on `raceStartTime.HasValue`. Logs each clamp. For normal (post-gun) starters `Max(startMat, gun) = startMat` → no change. This makes every non-start checkpoint's `NetTime = finishChip − gun = GunTime` for early starters → fixes 2242/2127.
- **Selection direction unchanged (already correct):** start=LAST / finish=FIRST / intermediate=FIRST were already implemented at `2080-2088` (start identified by min `DistanceFromStart`). Not touched.
- **⚠️ Item-2 verification — FINDING (NOT clean, pre-existing, NOT fixed this pass):** the start-checkpoint's OWN row sets `netTime = gunTime` (`2151-2156`) using the RAW `gunTime = startMat − gun`, which is **negative for early starters** — it does NOT consume the clamped baseline. So the start ROW stores negative Net/Gun for 2242/2127. This is pre-existing (was negative before) and does NOT affect the headline finish-time bug (the start row isn't a `finishReading`, so it skips the `negativeGunTimes` Fail-gate at `2446`; its split is already skipped by the `splitTimeMs < 0` guard at `4605`). Logical-correct value would be `0` (clamp start-row net to `max(0, startMat − clampedBaseline)`). **Left for user decision / fold into BUG-25.**
- **Decision (C) — single-mat (startCheckpointId == finishCheckpointId):** code-level — occurs only when a race has ONE checkpoint (or all checkpoints at the same `DistanceFromStart`), since start = min-distance Id and finish = max-distance Id. In that case the single group hits `isStartCheckpoint == true` → picks **LAST** → a single-mat finish would wrongly take the last crossing instead of FIRST. **Could not query prod DB from this sandbox** — user to run the SQL below to confirm whether any real race hits it. Documented only, not fixed (out of Phase A scope).
  ```sql
  -- Races whose only distinct checkpoint distance is one value → start Id == finish Id (single logical mat)
  SELECT c.RaceId, COUNT(DISTINCT c.Id) AS CheckpointCount,
         COUNT(DISTINCT c.DistanceFromStart) AS DistinctDistances
  FROM Checkpoints c
  WHERE c.IsActive = 1 AND c.IsDeleted = 0
  GROUP BY c.RaceId
  HAVING COUNT(DISTINCT c.DistanceFromStart) = 1;
  ```
- **Build:** `Runnatics.Services` ✅ 0 errors (14 pre-existing warnings). **Tests:** assigner ✅ 19/19 (`DOTNET_ROLL_FORWARD=LatestMajor` for net8.0).
- **➡️ BUG-25 ordering:** BUG-25 (start-row split = 00:00:00) MUST build on this clamped baseline — clamp first (done), then BUG-25. BUG-25 can also resolve the item-2 start-row negative-net finding (set start net/split to 0).
- **Prod verify (after deploy):** reprocess RaceId 47, then confirm 2242/2127 have `NetTime ≤ GunTime` (Chip ≤ Gun) on `Results`.

### 2026-06-13 — BUG-24 (public grouped leaderboard not honouring Leaderboard Settings) — Opus EXECUTE (backend half)

- **Root cause (NOT settings resolution — that was correct):** `PublicResultsService.GetPublicGroupedLeaderboardAsync` resolved settings via the right hierarchy (race row where `OverrideSettings==true`, else event row `RaceId==null`, else defaults — same chain as `GetEffectivePublicLeaderboardSettingsAsync`), but **mis-applied** them. A single `rankOnNet = SortByOverallChipTime` drove BOTH Overall and Category sort → Category ignored `SortByCategory*`; `NumberOfResultsToShowOverall` was never read (Overall used pageSize paging); `ShowOverallResults`/`ShowCategoryResults` were never read and the DTO didn't carry them.
- **Settings storage:** one `LeaderboardSettings` table; event row = `RaceId NULL`, race row = `RaceId set` + `OverrideSettings` bool. Columns: `Show{Overall,Category}Results`, `SortBy{Overall,Category}{Chip,Gun}Time`, `NumberOfResultsToShow{Overall,Category}`, `OverrideSettings`.
- **Fixes (backend, approved scope):**
  1. **Independent sort:** `overallRankOnNet` (←`SortByOverallChipTime ?? true`) drives Overall sort + podium + top-level `RankBy`; `categoryRankOnNet` (←`SortByCategoryChipTime ?? true`) drives category `OrderBy` + per-category `RankBy` label.
  2. **Independent counts:** `categoryTopN` from `NumberOfResultsToShowCategory` (keeps historical default 3 when unset, non-showAll); new `overallTopN` from `NumberOfResultsToShowOverall` caps `OverallResults` (paging disabled when capped); no cap when `showAll`.
  3. **Show toggles:** new `ShowOverall`/`ShowCategory` bools on `PublicGroupedLeaderboardDto`; when OFF the section's list is built empty so the public page hides it.
  4. **Per-section labels:** new `OverallRankBy`/`CategoryRankBy` (additive). Both use the **no-space `"ChipTime"`/`"GunTime"`** format — the form the FE already string-matches on top-level `RankBy` (per BUG-08 review; the spaced `"Chip time"` on `PublicCategoryGroupDto.RankBy` is never consumed and was left as-is).
- **Files modified:** `PublicResultsService.cs` (`GetPublicGroupedLeaderboardAsync` only), `Public/PublicGroupedLeaderboardDto.cs` (4 additive fields).
- **Out of scope (frontend):** the flat `/results` page (`GetPublicEventResultsAsync`) already returns `LeaderboardSettings` to the FE correctly and applies nothing itself — if that page misbehaves it's a UI fix (`C:\Projects\Runnatics.UI`, not in this workspace).
- **✅ UI half DONE (2026-06-13, repo `C:\Projects\Runnatics.UI\Runnatics.Ui`):** consumes the new fields.
  - `src/api/publicApi.ts` — added `overallRankBy?`, `categoryRankBy?`, `showOverall?`, `showCategory?` to `GroupedLeaderboardResponse` (all optional → old responses behave as before).
  - `EventResultsPage.tsx` (real Overall table + Age Category) — split `isGunTime` into `isGunTimeOverall` (←`overallRankBy ?? rankBy`) and `isGunTimeCategory` (←`categoryRankBy ?? rankBy`); Overall podium/table use the overall flag, Age Category uses the category flag; Overall section + its Pagination gated on `showOverall`, Age Category on `showCategory` (default true via `!== false`).
  - `LeaderboardPage.tsx` & `GlobalResultsPage.tsx` (podium + male/female category columns) — category columns use `categoryRankBy`; podium gated on `showOverall`; category grid gated on `showCategory`.
  - **Build:** `npm run build` (Vite) ✅ built in ~13s, all bundles emitted. (Raw `tsc -p tsconfig.app.json` floods pre-existing project-wide `@/`-alias + verbatimModuleSyntax errors unrelated to this change — Vite is the source of truth, per prior rounds.)
  - **Not committed yet** — awaiting user go-ahead to push the UI repo.
- **Build:** `Runnatics.Services` ✅ 0 errors (14 pre-existing warnings). **Trace (event SRWYS41SkT, event-level row, override OFF):** Overall→Chip/cap5/shown, Category→**Gun**/cap5/shown, sorts diverge. ✓
- **No SQL** (all columns already exist).

### 2026-06-13 — SplitTimes/ReadNormalized stale-row fix (Clear gate completion) — Opus EXECUTE

- **Unifying root cause (user-confirmed via prod RaceId 47):** the ENTIRE RFID pipeline is insert-only / skip-if-exists, so no shipped fix (BUG-04 SegmentTime, BUG-27 gun clamp) ever reaches data that was already processed:
  - `DeduplicateAndNormalizeAsync` filters out raw reads that already have a `ReadNormalized` (`~1855-1864`) → BUG-27 clamp never re-fired on Bibs 2242/2127.
  - `CreateSplitTimesFromNormalizedReadingsAsync` skips any `(ParticipantId, CheckpointId)` that already has a SplitTime (`~4578-4588`) → Bib 2262's May 5.25/15.75 km rows (old code, `SegmentTime == SplitTimeMs`) survived while the other 7 checkpoints got correct rebuilt rows.
  - The intended escape hatch (`ClearProcessedDataAsync`, and per-participant `ReprocessParticipantsAsync`) deleted Results + ReadNormalized + assignments but **NOT SplitTimes** → even a `forceReprocess=true` rebuild left the stale splits, and the splits skip-guard then skipped them.
- **🏛️ ARCHITECTURAL DECISION (user-approved):** the skip-guards are the **live-ingest concurrency-safety mechanism** — `LiveReadingService:155` (Raspberry Pi) and other incremental paths call `ProcessCompleteWorkflowAsync` WITHOUT a Clear; insert-only/skip-existing keeps each live append cheap and idempotent (concurrent appends are safe). **A full rebuild is correctly gated by `forceReprocess=true` → `ClearProcessedDataAsync`, NOT by removing the skip-guards.** Removing the guards (the originally-proposed Fix 1/Fix 3) would make every Pi batch delete-and-rebuild the whole race's normalized data mid-event — the corruption risk we explicitly avoided. **So the fix completes the Clear gate; it does NOT touch the guards.**
- **Fixes (2 sites in `RFIDImportService`, both hard-delete to match the existing sibling `BulkDeleteAsync`/`DeleteRangeAsync` — these are explicit destructive resets, decision (A)):**
  1. **FIX A — `ClearProcessedDataAsync`** (new step "2b"): hard-delete all SplitTimes for the race (scoped `st.EventId == … && st.Participant.RaceId == …`, unfiltered on IsActive/IsDeleted so soft-deleted orphans are cleaned too), mirroring the ReadNormalized step. Added `SplitTimesCleared` to `ClearDataResponse` (+ Summary line).
  2. **FIX B — `ReprocessParticipantsAsync`** (new step "2b"): hard-delete the targeted participants' SplitTimes (`validParticipantIds.Contains(st.ParticipantId)`), mirroring their ReadNormalized `DeleteRangeAsync`, so the per-participant "Process Result" button also yields clean splits. Added `SplitTimesCleared` to `ReprocessParticipantsResponse`.
- **NOT changed (deliberately):** dedup skip-guard (`~1855`), splits skip-guard (`~4578`), and the existing ReadNormalized/Results delete semantics. Soft-delete remains correct only for the BUG-06 race-move rebuild (not a destructive reset).
- **Build:** `Runnatics.Services` ✅ 0 errors. **Tests:** assigner ✅ 19/19 (`DOTNET_ROLL_FORWARD=LatestMajor`).
- **▶️ RUNBOOK — repair prod RaceId 47 (and any race with stale timing data) AFTER deploy:**
  1. **Deploy** this build.
  2. **Full reprocess:** `POST /api/rfid/{eventId}/{raceId}/process-all?forceReprocess=true` — Clear now wipes Results + ReadNormalized + **SplitTimes** + assignments → `ProcessCompleteWorkflowAsync` rebuilds everything from raw reads (empty tables → nothing to skip → BUG-27 clamp fires on every ReadNormalized, SegmentTime rebuilt on every SplitTime). (Manual equivalent: `DELETE …/clear-processed-data?keepUploads=true` then `POST …/process-all`.)
  3. **Verify:** (a) Bibs 2242/2127 `NetTime ≤ GunTime` (Chip ≤ Gun — clamp fired on rebuilt ReadNormalized); (b) Bib 2262 5.25/15.75 km `SegmentTime ≠ SplitTimeMs` (splits rebuilt with BUG-04 distance-chaining). Start-row split `= 0` is **BUG-25, separate** (still pending; must build on the BUG-27 clamped baseline).
  - ⚠️ Do NOT use a plain (non-force) reprocess to repair stale data — by design it preserves already-processed rows (live-ingest semantics). Repair REQUIRES the Clear gate.

### ▶️ NEXT — BUG-25 remaining surfaces (page-scoped public split details DONE 2026-06-13, `f5f148b`)

- **BUG-25 (GLOBAL split/race-time rules — not race-specific).** Rules confirmed by user:
  - **Start checkpoint (`DistanceFromStart = 0`):** Split Time = `00:00:00` (baseline, no prior checkpoint); Race Time = `00:00:00` (net-from-start-line is zero by definition).
  - **Every subsequent checkpoint:** Split Time = this checkpoint time − previous checkpoint time; Race Time = this checkpoint time − start-line crossing time.
  - Applies to EVERY place splits are calculated or displayed.
- **DONE:** ✅ **public participant split details page** — `PublicResultsService.GetPublicParticipantDetailAsync` now zeroes the start row (Split=Race=`00:00:00`, speed `—`) and computes segment-based speed (`f5f148b`, 2026-06-13).
- **✅ PROD VERIFIED (2026-06-13):** Multiple participants confirmed — Start row `00:00:00`/`00:00:00`/`—`; all segment speeds in realistic running range (10–15 km/h); Race Time increases consistently row to row; Split Times sensible per segment. Public-facing results correct and ready for Punit/Deepender to test the full flow.
- **➡️ REMAINING SURFACES (lower priority — internal/admin only):**
  1. **admin participant detail / BIB drill-down** — start row = `00:00:00`/`00:00:00`; segment split times.
  2. **leaderboard split view**.
  3. **results export (Excel)** (`ResultsExportService`).
  4. **public grouped leaderboard per-participant drill-down**.
  - Pattern to reuse: distance-keyed start-row detection (`DistanceFromStart == 0`), Split=`SegmentTime` (start→0), Race=`SplitTimeMs` (start→0); see `f5f148b` for the reference implementation.
  - ⚠️ Also fold in the latent **pace-semantics inconsistency** flagged 2026-06-13: `ResultsService.CalculateResultsAsync:167` writes `SplitTimes.Pace` as a *cumulative* avg pace while `RecordManualTimeAsync` writes a *segment* pace — decide one definition and apply consistently while touching these surfaces.

- **BUG-24 — FULLY DONE** (backend `9b9f510`/`80ee3e9`; UI `3860638`, both merged to master 2026-06-13).
  - **UI changes (GlobalResultsPage.tsx only, commit `3860638`):**
    1. **Category cap:** dropped `showAll: true` from `getGroupedLeaderboard` call → backend now applies `NumberOfResultsToShowCategory` per age-bracket.
    2. **Finisher header removed:** deleted the `{data?.totalFinishers} finishers` block from the Leaderboard header. Participant-stats "of N" on `ParticipantDetailPage` is a different endpoint — left untouched.
    3. **Gender filter:** replaced `getResultBrackets` cascade (age-category dropdown) with a hardcoded Male/Female dropdown; renamed `bracket`→`gender` state; passes `gender:` to API (not `category:`). Selecting Male/Female filters the backend's `genderCategories` → grid shows only that gender's age-bracket columns; both genders shown by default.
    4. **Dual podium:** replaced `derivePodium` (single mixed-gender pool) with `derivePodiumForGender(genderCategories, targetGender, overallRankBy)`; derive `malePodium` + `femalePodium` separately; render both side-by-side in a grid (Male label | Female label); only show the matching gender's podium when filter is active. Now reads `overallRankBy ?? rankBy` (was missing — `overallRankBy` was never consumed) → podium sort honours Overall sort setting independently of Category sort.

### 2026-05-29 — backend-agent — Live Timing Ingest (Raspberry Pi → API)

- **Branch**: `master`
- **What was built**: `POST /api/rfid/{eventId}/{raceId}/live-readings` — receives flat RFID readings from a Raspberry Pi timing mat, saves to DB, pushes SignalR crossing events, then triggers the full RFID processing pipeline asynchronously to produce live rankings.
- **Files created**:
  - `Runnatics.Models.Client/Requests/RFID/LiveReadingDto.cs` — single reading: `Epc, Time (Unix ms), Antenna, Rssi, Channel`
  - `Runnatics.Models.Client/Requests/RFID/LiveReadingsRequest.cs` — batch body: `DeviceId (MAC) + List<LiveReadingDto>`
  - `Runnatics.Models.Client/Responses/RFID/LiveReadingResponse.cs` — response: `Accepted, Skipped, BatchId (encrypted)`
  - `Runnatics.Services.Interface/ILiveReadingService.cs` — interface with `IngestAsync(eventId, raceId, request, ct)`
  - `Runnatics.Services/LiveReadingService.cs` — full implementation
- **Files modified**:
  - `Runnatics.Api/Controller/RFIDController.cs` — added `IngestLiveReadings` action, injected `ILiveReadingService`
  - `Runnatics.Api/Program.cs` — registered `ILiveReadingService`; added `X-Device-Key` middleware (placed before the existing `X-Public-Key` guard)
  - `Runnatics.Api/appsettings.json` — added `DeviceApi:Key = "SET_IN_AZURE_ENV_VARS"`
- **Authentication**: `X-Device-Key` header validated in inline middleware (same pattern as `X-Public-Key`). Azure env var: `DeviceApi__Key`. Dynamic IP → API key is the sole auth mechanism (no Azure IP allowlist).
- **Processing flow**:
  1. Decrypt eventId + raceId → load Event (TenantId, timezone) → resolve Device by MAC
  2. Get/create today's `UploadBatch` (SourceType = `"online_webhook"`, IsLiveSync = true, FileFormat = `"LIVE"`)
  3. Map `LiveReadingDto` → `RawRFIDReading` (ProcessResult = `"Pending"`, same schema as offline pipeline)
  4. Save to DB, update batch stats
  5. Push immediate SignalR `CheckpointCrossings` events (EPC → Participant lookup)
  6. Fire-and-forget: new DI scope → `IRFIDImportService.ProcessCompleteWorkflowAsync(eventId, raceId)` → dedup → normalize → split times → per-participant rankings → SignalR push
- **Key decisions**:
  - Reuses existing `UploadBatch` + `RawRFIDReading` tables — no schema changes needed
  - One batch per device per race per day (keyed: `DeviceId + EventId + RaceId + Date`)
  - `AuditProperties.CreatedBy = 0` (system) since request is device-authenticated, not user-authenticated
  - Fire-and-forget uses `IServiceScopeFactory` to create a fresh scope so scoped services are not disposed
  - `ProcessCompleteWorkflowAsync` is idempotent (dedup handles re-runs); concurrent runs are safe
  - `deviceId` in request body is the Pi's MAC address — must match a registered `Device.DeviceMacAddress` (normalized: lowercase, no colons)
- **Pi request format**:
  ```
  POST /api/rfid/{encryptedEventId}/{encryptedRaceId}/live-readings
  X-Device-Key: <secret>
  { "deviceId": "00162512dbb0", "readings": [{ "epc": "...", "time": 1777163620641, "antenna": 2, "rssi": -74.0, "channel": 2 }] }
  ```

### 2026-05-15 — backend-agent — Testing Feedback Round 1 (BUG API-1 through API-13)

- **Branch**: `bugfix/testing-round-1`
- **What was built**: 11 bugs fixed across RFID processing, manual timing, public leaderboard, and dashboard.
- **Schema changes** (`db/scripts/TestingFeedback_Round1_SchemaChanges_20260515.sql`):
  - `Participants`: `ManualDistance DECIMAL(8,3)`, gender normalization (M/F)
  - `Checkpoints`: `IsMandatory BIT DEFAULT 1`
  - `Races`: `IsTimed BIT DEFAULT 1`
  - `RawRFIDReadings`: `IsMultipleEpc BIT DEFAULT 0`
  - `UploadBatches`: removed unique index on FileHash, added `TotalTagsInFile INT`, `TagsProcessed INT`
  - Performance indexes on Participants and RawRFIDReadings
- **BUG API-1**: `GET /api/participants/{eventId}/{raceId}/{participantId}/detections` — participant RFID detections grouped by checkpoint (`ParticipantDetectionsResponse`, `GetDetectionsAsync` in `ParticipantImportService`)
- **BUG API-2 + API-11**: MultipleEPC detection (comma/pipe in EPC string → `IsMultipleEpc=true`, `ProcessResult="MultipleEPC"`); skip EPC→participant mapping for multi-EPC rows; removed duplicate FileHash checks from both upload methods; `TotalTagsInFile`/`TagsProcessed` tracking; `IsMultipleEpc` added to `RfidRawReadingDto`
- **BUG API-3**: `RecordManualTimeAsync` now UPSERTS SplitTimes (creates row if missing, updates otherwise); accepts elapsed ms or IST-from-midnight (auto-detects); no longer errors for first-time manual entry
- **BUG API-6**: `PUT /api/participants/{eventId}/{raceId}/{participantId}/race-category` — changes AgeCategory and recalculates rankings; `ChangeParticipantCategoryAsync` in `ResultsService`
- **BUG API-7**: `POST /api/participants/{eventId}/{raceId}/{participantId}/process-result` — re-triggers ranking calc for one participant; `ProcessParticipantResultAsync` + shared `ReprocessParticipantInternalAsync` in `ResultsService`
- **BUG API-8 + API-10**: Fixed gender filter ("M"/"F" normalized to "Male"/"Female" for comparison and display); fixed race filter from `Contains` to exact `==` (prevents cross-race contamination); gender grouping in leaderboard also normalized
- **BUG API-9**: RFID `ProcessRFIDImportAsync` now checks `Race.IsTimed`; if `false` returns `Status=Skipped` without EPC→participant mapping
- **BUG API-12**: SupportQuery — all 7 endpoints confirmed fully functional; bug is UI-side (no backend change)
- **BUG API-13**: Added `GET /api/dashboard/event/{eventId}/stats` and `GET /api/dashboard/race/{eventId}/{raceId}/stats` returning `EventDashboardStatsDto` / `RaceDashboardStatsDto` with registrations, finishers, DNF/DNS, gender/category breakdowns
- **Files created**:
  - `Runnatics.Models.Client/Requests/Participant/ChangeRaceCategoryRequest.cs`
  - `Runnatics.Models.Client/Responses/Participants/ParticipantDetectionsResponse.cs`
  - `Runnatics.Models.Client/Responses/RFID/ReaderFileUploadResponse.cs`
  - `Runnatics.Models.Client/Responses/Dashboard/EventDashboardStatsDto.cs`
  - `db/scripts/TestingFeedback_Round1_SchemaChanges_20260515.sql`
- **Key decisions**:
  - `IdEncryptor` AutoMapper converter only handles `int`; for `long` RawRFIDReading IDs use `_encryptionService.Encrypt(id.ToString())` directly
  - BUG API-4 is covered by API-1 shape (no DISTINCT/GroupBy — all detections shown)
  - BUG API-5 (split time segment calculation fix) and BUG API-14 (performance hardening) are not yet implemented
- **Pending**:
  - BUG API-5: Split time correctness (segment = current − previous chip time; IsMandatory-based Finished/DNF status)
  - BUG API-14: Performance hardening (Brotli/Gzip compression, output cache on public endpoints, WAF note in DEPLOYMENT.md)
  - Run `db/scripts/TestingFeedback_Round1_SchemaChanges_20260515.sql` against Azure SQL before deploying

### 2026-06-09 — Bug Fix Round 2 (BUG-01, BUG-02, BUG-03)

- **BUG-01: Multiple Tags Auto-Map**
  - **API fix** (`RfidReaderService.cs`): When debounce batch fires with `Count > 1`, now sends single `MultipleEpcDetected(string[] epcs)` SignalR event instead of N individual `EpcDetected` events.
  - **UI fixes**:
    - `useBibMappingHub.ts`: Added `multipleEpcEpcs: string[] | null` state + `MultipleEpcDetected` handler.
    - `useBibMappingRows.ts`: Added `setMultipleEpcError(participantId)` function — sets `status: 'error', isMultipleEpc: true` on a row. Added `override.isMultipleEpc` to rows memo so override takes precedence over server value.
    - `BibMapping.tsx`: Wired up `useBibMappingHub`, watches `multipleEpcEpcs` → calls `setMultipleEpcError(focusedRowId)`. Added 500ms lockout on `handleSubmit` — if a second submission arrives within 500ms of the last successful map, it's rejected as multiple EPC (guards against USB keyboard reader rapid EPC1+Enter → EPC2+Enter pattern).

- **BUG-02: BIB Numbers Not Sequential**
  - Root cause: `OrderBy(p => p.BibNumber)` on string column → lexicographic sort ("1","10","11","2").
  - Fixed ALL four sort locations with length-first approach: `.OrderBy(p => p.BibNumber == null ? 0 : p.BibNumber.Length).ThenBy(p => p.BibNumber)` (EF Core translates this to `ORDER BY CASE WHEN ... THEN 0 ELSE LEN(BibNumber) END, BibNumber`).
  - Files modified: `BibMappingService.cs` (line 568), `ParticipantImportService.cs` (lines 1799, 475, 1987).

- **BUG-03: Checkpoint Creation Generic Error**
  - **Root cause #1**: Duplicate `CreateMap<CheckpointRequest, Checkpoint>()` in `AutoMapperMappingProfile.cs` — second registration (lines 421-427) overwrote the complete first one (363-374). Second was missing `AuditProperties`, `Device`, `ParentDevice` ignore rules. **Fix**: Removed the duplicate registration.
  - **Root cause #2**: `CheckpointsService.Create` catch block set `ErrorMessage = "Error creating checkpoint."` with no details. **Fix**: Now sets `ErrorMessage = $"Error creating checkpoint: {ex.Message}"`.
  - **⚠️ BLOCKING**: The most likely actual runtime failure is still `SqlException: Invalid column name 'IsMandatory'` because `db/scripts/TestingFeedback_Round1_SchemaChanges_20260515.sql` has NOT been run against Azure SQL. Run this script manually before testing checkpoint creation.

- **Files modified**:
  - `Runnatics.Services/RfidReaderService.cs`
  - `Runnatics.Services/CheckpointsService.cs`
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs`
  - `Runnatics.Services/BibMappingService.cs`
  - `Runnatics.Services/ParticipantImportService.cs`
  - `src/main/src/hooks/useBibMappingHub.ts`
  - `src/main/src/pages/admin/bibMapping/useBibMappingRows.ts`
  - `src/main/src/pages/admin/bibMapping/BibMapping.tsx`

### 2026-06-09 — REVIEW + VERIFY findings (Bug Fix Round 2)

- **Result: FAIL.** Both `dotnet build` and `npm run build` pass (0 errors) but the build does NOT catch the headline defect.
- **🔴 BLOCKER 1 (BUG-01, `BibMapping.tsx`)**: `useEffect` at lines 162-166 references `focusedRowId` in its dependency array, but `focusedRowId` (useState) is declared later at line 172. The deps array is evaluated during render → Temporal Dead Zone → `ReferenceError: Cannot access 'focusedRowId' before initialization`. **The BIB Mapping page crashes/white-screens on every mount.** Fix: move `useBibMappingHub()` + the effect below the `focusedRowId` declaration.
- **🔴 BLOCKER 2 (BUG-01, `useBibMappingHub.ts` + `BibMapping.tsx`)**: `multipleEpcEpcs` is set on `MultipleEpcDetected` but never reset. Effect depends on `[multipleEpcEpcs, focusedRowId]`, so after one real multi-EPC event, every later focus change re-fires the effect and falsely flags the newly focused row as Multiple-EPC. Also does NOT reset on SignalR reconnect. Fix: expose `clearMultipleEpc()` from the hub and call it after consuming the event.
- **🟡 RISK 3 (BUG-01)**: The 500ms lockout calls `setMultipleEpcError`, which sets `isMultipleEpc: true`. That row then renders a static badge with disabled input/skip and the override survives refetch (prune keeps `status:'error'`). A legitimate fast scan (~2/sec) permanently bricks the BIB with no UI recovery. Recommend transient `flashError` instead, or add a clear/retry affordance.
- **🟡 RISK 4 (BUG-03, security)**: `CheckpointsController` returns `ErrorMessage` (now containing `ex.Message`) in the 500 body. SQL exceptions can leak table/column or server/instance names. Approved as-is, but consider gating raw `ex.Message` to non-prod.
- **✅ BUG-01 server side correct** (`RfidReaderService.cs`): `batch.Count > 1` → single `MultipleEpcDetected`; SignalR typing matches client.
- **✅ BUG-02 FIXED**: All 4 sort sites identical (`BibMappingService.cs:568`, `ParticipantImportService.cs:475/1800/1989`); verified `1,2,9,10,11,20,100` orders correctly. Minor: alphanumeric BIBs (`A1/A10/B2`) order by length-then-lexical (acceptable).
- **✅ BUG-03 code FIXED** (duplicate AutoMapper map removed; error surfaced) but **still BLOCKED on running `TestingFeedback_Round1_SchemaChanges_20260515.sql`** (adds `IsMandatory`).
- **No files were modified during this review phase** (CLAUDE.md Rule 4). Blockers 1+2 and Risk 3 are pending a follow-up EXECUTE pass.

### 2026-06-09 — EXECUTE pass 2 — BUG-01 review fixes (Blockers 1+2, Risk 3)

- **FIX 1 (Blocker 1 — TDZ crash)**: Moved `useBibMappingHub()` + its `useEffect` in `BibMapping.tsx` to BELOW the `focusedRowId` useState declaration (now at lines 163/168-178). The deps array no longer reads `focusedRowId` before initialization → no more render crash.
- **FIX 2 (Blocker 2 — stale multipleEpcEpcs)**: Added `clearMultipleEpc()` to `useBibMappingHub` (sets `multipleEpcEpcs` back to null). The `BibMapping.tsx` effect now calls `clearMultipleEpc()` immediately after consuming the event, so a later focus change can't re-fire it. Also reset `multipleEpcEpcs` to null in the hub's `onreconnected` handler.
- **FIX 3 (Risk 3 — sticky lockout)**: The 500ms USB lockout in `handleSubmit` now silently `return`s instead of calling `setMultipleEpcError`. It's a debounce gate, not an error state — the row stays mappable once the window expires. Removed the now-unused `setMultipleEpcError` from `handleSubmit`'s dep array (still used by the multi-EPC effect).
- **Files modified**: `src/main/src/hooks/useBibMappingHub.ts`, `src/main/src/pages/admin/bibMapping/BibMapping.tsx` (BUG-01 scope only — BUG-02/03 files untouched).
- **Builds**: `npm run build` ✅ 0 errors · `dotnet build` ✅ 0 errors.
- **Re-trace**: TCP multi-tag → one row flagged then state cleared (no poisoning of later rows). USB rapid-scan → 2nd scan silently gated, row stays mappable; deliberate re-scan after window maps normally; first scan of session passes (ref starts at 0). **BUG-01 now FIXED.**

### 2026-06-09 — Bug Fix Round 2 / Round 2 (BUG-04, BUG-05, BUG-07)

- **Key finding**: the bulk pipeline (`RFIDImportService`) and `ResultsService` have TWO divergent implementations of split-time and result calculation. The pipeline (runs after every upload) used the wrong logic; the correct logic already existed in `ResultsService`. Fixes align the pipeline with the correct logic.

- **BUG-04 (split/cumulative)** — `RFIDImportService.CreateSplitTimesFromNormalizedReadingsAsync`:
  - Root cause: split creation ordered each participant's readings by `ChipTime`, so stored `SegmentTime` chained in clock order, while every display path orders by `Checkpoint.DistanceFromStart` and prefers the stored `SegmentTime` → segments didn't sum to the displayed cumulative when chip-time order ≠ distance order (missed checkpoints, loops, clock skew).
  - Fix: order readings by `Checkpoint.DistanceFromStart` then `ChipTime` (both the main query and the per-participant re-sort). `SplitTimeMs` (cumulative) stays `ChipTime − raceStart`. Display unchanged.
  - Decision (user): Cumulative = net-from-start-line (chip time); start row ~0. Display already does this — no display change needed.

- **BUG-05 (DNF/Finished)** — `RFIDImportService.CalculateRaceResultsAsync`:
  - Root cause: status keyed only on presence at the single max-distance finish checkpoint; `IsMandatory` ignored. (Correct rule already in `ResultsService.ComputeParticipantStatusAsync`.)
  - Fix: load mandatory checkpoint ids (fallback = highest-distance checkpoint if none flagged); `Finished` = ReadNormalized covers ALL mandatory ids; `DNS` = no start reading; `DNF` = started but missing ≥1 mandatory. Finish time/ranking now from the highest mandatory checkpoint (`finishGateCheckpoint`), ranked by GunTime. Backward compatible when no mandatory flagged.

- **BUG-07 (wrong participant in wrong race)** — `PublicResultsService.GetPublicResultsAsync`:
  - Root cause: public Overall results filtered by `r.Race.Title` string (or not at all when no race selected) and sorted by per-race `OverallRank`, merging races so a 5KM Rank-1 leaked into the 10KM list. (Admin `GetLeaderboardAsync` and `GetPublicGroupedLeaderboardAsync` are correctly RaceId-scoped — not the leak.)
  - Fix: changed signature to `int? raceId` and filter `r.RaceId == raceId.Value`; `GetPublicEventResultsAsync` passes the resolved `selectedRace?.Id`; bib-lookup keeps `raceId: null` (intentional cross-race bib search).
  - Decision (user): public Overall page should use per-race tabs (each scoped by RaceId), Male/Female split, Overall section before Age Category — primarily a FRONTEND change, still pending in the UI.

- **Stale-data audit (race move, `ParticipantImportService.UpdateParticipantExtendedAsync`)**: race reassignment correctly migrates Results/SplitTimes/ReadNormalized/ChipAssignment to the new participant, sets `Results.RaceId → target`, recalcs both races' ranks, soft-deletes the old participant. **No orphaned rows remain under the old RaceId** → not a BUG-07 cause.
  - ⚠️ **Flagged for BUG-06 (not fixed here)**: migrated `SplitTimes`/`ReadNormalized` keep their **source-race `CheckpointId`**, so after a move the timing data references the wrong race's checkpoints (orphaned) → moved participant shows missing splits / wrong status in the new race. Needs checkpoint remapping by distance — belongs to BUG-06 data-transfer scope.

- **Files modified**: `RFIDImportService.cs` (BUG-04 + BUG-05), `PublicResultsService.cs` (BUG-07).
- **Build**: `dotnet build` ✅ 0 errors.
- **Pending**: BUG-07 frontend (race tabs / M-F split / section order); BUG-06 checkpoint-remap on race move.

### 2026-06-09 — REVIEW + VERIFY findings (BUG-04, BUG-05, BUG-07)

- **Result: PASS for all traced scenarios** (`dotnet build` ✅ 0 errors). Splits 0/5/10/21.1 chain correctly and sum to cumulative; missed-mandatory → DNF; missed non-mandatory → Finished; public 10 KM page shows only 10 KM. No DTO changes; per-participant reprocess + admin leaderboard unaffected (now consistent with the bulk pipeline).
- **🟠 FLAW I introduced (BUG-07 edge case), NOT yet fixed** — `PublicResultsService.GetPublicEventResultsAsync` (~line 86-92): when a race title is supplied but does NOT resolve to a published race, `selectedRace` is null → `selectedRace?.Id` null → `GetPublicResultsAsync` applies no race filter → returns ALL races merged (re-introducing the cross-race leak). Old `Race.Title == raceName` returned empty in that case. Recommended fix: when `race` is non-empty but `selectedRace == null`, short-circuit to empty published results. Happy path (valid title) is correct.
- **Edge cases verified safe**: `DistanceFromStart` is non-nullable `decimal` (no NULL ordering risk); `IsMandatory` non-nullable bool; zero-checkpoints guarded (Failed) before mandatory logic; `GetPublicResultByBibAsync` keeps `raceId: null` (intentional cross-race bib search).
- **Pre-existing risks (NOT introduced, flagging only)**: (1) if every checkpoint is a child (`ParentDeviceId` set), `parentCheckpoints.First()` in `CalculateRaceResultsAsync` throws; (2) within a single batch, two readings at the same checkpoint can create duplicate `SplitTimes` rows (dedup key only checks pre-existing DB rows).
- **✅ BUG-07 unresolved-race flaw FIXED**: `GetPublicEventResultsAsync` now short-circuits to an empty published result set when `race` is non-empty but `selectedRace == null` (race not found / unpublished), instead of falling through to an unfiltered all-races query. Happy path (valid title → filter by RaceId) and no-race-selected path (race empty → all races, backward compat) unchanged. `dotnet build` ✅ 0 errors.

### 2026-06-09 — Bug Fix Round 2 / Round 3 (BUG-06, BUG-14)

- **BUG-06 (race-category change — data transfer + reprocess)** — `ParticipantImportService.UpdateParticipantExtendedAsync` race-move block:
  - Injected `IRFIDImportService` into `ParticipantImportService` (no circular dep — RFIDImportService doesn't reference it; both already registered in Program.cs 229/234).
  - Build a `sourceCheckpointId → targetCheckpointId` map by `DistanceFromStart` (Name tiebreak) before the move transaction.
  - Inside the transaction: **soft-delete** the migrated `SplitTimes` (rebuilt later); reassign `ReadNormalized` to the new participant AND **remap `CheckpointId`** to the target race's equivalent; readings with no target equivalent are **soft-deleted**. Kept old-race rank recalc; removed the redundant target-race rank recalc.
  - After the transaction commits (not nested): call `_rfidImportService.CreateSplitTimesFromNormalizedReadingsAsync` + `CalculateRaceResultsAsync` for the **target race** (encrypted `participant.EventId` + encrypted `targetRaceId`) → rebuilds splits against the target's gun start and recomputes mandatory-aware status/ranks. Reprocess failures are logged, not fatal (the move already committed).
  - Decisions (user): full reprocess via the pipeline; soft-delete orphaned readings; reprocess TARGET race only.
  - "Process Result" button already exists (UI + `ProcessParticipantResultAsync`) — no new endpoint.

- **BUG-14 (manual time edit)**:
  - **Backend guard** (`ResultsService.RecordManualTimeAsync`): if `race.IsTimed` and the participant has no active `ChipAssignment` (`UnassignedAt == null && IsActive && !IsDeleted`) → set `ErrorMessage` ("Map an EPC chip… before recording a manual time for a timed race.") and return null (no throw). Applies to ALL checkpoints.
  - **Controller** (`RFIDController.RecordManualTime`): added `ErrorMessage.Contains("EPC")` to the BadRequest branch so the guard returns 400 (not 500); the UI already surfaces `error.message` in the snackbar.
  - **DTO** (`CheckpointTimeInfo`): added encrypted `CheckpointId`, populated in `ResultsService.LoadCheckpointTimesAsync` (loops over every active race checkpoint) — gives the UI an addressable id for checkpoints with no split.
  - **UI** (`ParticipantDetail.tsx` + `CheckpointTime.ts` model): the splits/manual-time table now iterates the full `participant.checkpointTimes` list (merging the matching split by name) instead of `participant.splitTimes`. Every existing checkpoint — including missed ones and newly created ones — now renders an editable manual-time row keyed by the encrypted `checkpointId`. Satisfies "works for all checkpoints" + "auto-activates when a checkpoint is created". EPC-guard error shows via the existing snackbar.

- **Files modified**: `ParticipantImportService.cs`, `ResultsService.cs`, `RFIDController.cs`, `CheckpointTimeInfo.cs`, `ParticipantDetail.tsx`, `models/participants/CheckpointTime.ts`.
- **Builds**: `dotnet build` ✅ 0 errors · `npm run build` ✅ 0 errors.
- **Pending (still open from earlier rounds)**: BUG-07 frontend (race tabs / M-F split / section order).

### 2026-06-09 — REVIEW + VERIFY findings (BUG-06, BUG-14)

- **Result: PASS.** `dotnet build` ✅ 0 errors · `npm run build` ✅ 0 errors. No blocking flaws.
- **BUG-06 verified**: 21.1KM→10KM move soft-deletes 15/21.1km readings (no target equivalent), remaps 0/5/10km to new participant + new CheckpointId; exact-distance match only (different distances drop their readings, per approved design); DI of IRFIDImportService is acyclic; SplitTimes soft-delete uses correct audit pattern; ReadNormalized reassignment sets ParticipantId=newParticipant.Id AND remapped CheckpointId.
- **BUG-06 limitation (documented, acceptable)**: the post-commit reprocess (`CreateSplitTimes`/`CalculateRaceResults`) returns `Status="Failed"` rather than throwing, so a reprocess failure can't roll back the committed move — it leaves the moved participant with remapped readings but stale/empty splits+ranks, recoverable via the existing "Process Result" button (logged as warning).
- **BUG-14 verified**: Timed + no chip → 400 with the EPC message in the snackbar; Timed + chip → succeeds; Non-timed → guard skipped; zero checkpoints → empty table (no crash); encrypted CheckpointId decrypted in the service, encrypted in the service. Normal (no race-change) update path unaffected; `CheckpointTimeInfo`/`CheckpointTime` change is additive (only one constructor site).
- **BUG-14 limitation (pre-existing, not introduced)**: split↔checkpoint merge is by checkpoint NAME, so loop races with duplicate checkpoint names could mis-attach a split row. Same approach as the prior code.

<!--
FORMAT:
### [Date] — [Agent] — [Feature/Task]
- **What was built**: ...
- **Files created/modified**: ...
- **Decisions made**: ...
- **Pending**: ...
-->

### 2026-06-09 — Bug Fix Round 5 (P2 polish — BUG-09/11/12/15/16/17/18/19/20/21/22/23 + 2 TS errors)

- **Result: EXECUTE complete.** API `dotnet build` ✅ 0 errors · UI `npx tsc --noEmit` ✅ 0 errors (both pre-existing TS errors fixed) · UI `npm run build` ✅. Decisions confirmed by user: BUG-11 show-all-rows; BUG-12 display/grouping-only (no import changes); BUG-16 column filter; BUG-09 keep race tabs.
- **Verify-only (no code), confirmed during research**: BUG-09 (race tabs already per-RaceId from Round 4), BUG-15 ("Detections by Checkpoint" table already shows every reading w/ reader name + timestamp), BUG-18 (backend already gates EPC on IsTimed — RFIDImportService:1036 skip, ResultsService:1419 manual-time chip guard; BibMapping never forces EPC), BUG-21 (public nav already uses `<a href>`/`<Link>`, no programmatic navigate in pages/public).

- **BUG-12 (no "Unknown" category)** — display/grouping layer only:
  - Backend: `PublicResultsService.cs:318` grouped leaderboard, `DashboardService.cs` event+race category breakdowns, `ResultsExportService.cs:219` Excel category sheet — all now `.Where(category not null/empty/"Unknown")` then group; uncategorized still appear in Overall.
  - `RFIDImportService.CalculateCategoryRankingsAsync` — only ranks finishers with a real category; explicitly nulls any stale `CategoryRank` on uncategorized finishers (so no misleading category rank shows).
  - UI defensive guard: `EventResultsPage`/`GlobalResultsPage`/`LeaderboardPage` filter out blank/"Unknown" category buckets before rendering.
  - Import write-defaults that store the literal "Unknown" were intentionally NOT touched (user decision — separate data-cleanup task).

- **BUG-17 (gender M/F)** — `PublicResultsService.MapToResultDto:1069` now emits raw "M"/"F" (was "Male"/"Female") → fixes the `/results` search list. `DashboardService` gender breakdown keys now raw "M"/"F". UI `ParticipantDetail.tsx` (3 sites: 872/1248/1354) show M/F; `ViewParticipants` grid Gender column gets a display-only `valueFormatter` (male→M, female→F, other→O; underlying value unchanged for filtering). Filter-dropdown OPTION labels left as "Male"/"Female" (selectors, not data display).

- **BUG-11 (remove "Show All Finishers")** — removed the button AND the 5-row cap in `EventResultsPage` `CategoryCard` and `GlobalResultsPage` `CategoryTable`; both now render all rows. `LeaderboardPage` already showed all.

- **BUG-16 (BIB drill-down columns)** — Gender column already existed. Added **Manual Distance** column (`agNumberColumnFilter`) to `ViewParticipants` AG-Grid + mapped `manualDistance` into rows. Backend: added `ManualDistance` (decimal?) to `ParticipantSearchReponse` — AutoMapper auto-maps it (search uses `_mapper.Map`, ParticipantImportService:499). No SQL (entity column already exists).

- **BUG-19 (reader re-upload + "X of Y tags")** — UI already supported re-upload + showed the snackbar, but it read `uploadedTags`/`totalTags` which the backend never returned (fell back to `totalReadings` → always "N of N"). Fix (backend): added `TotalTags` (=TotalTagsInFile) and `UploadedTags` (=valid distinct tags) to `RFIDImportResponse`, populated in both upload methods (`UploadRFIDFileAsync` + `UploadRFIDFileEventLevelAsync`). UI already consumes the camelCase fields → "X of Y" now accurate (X<Y when multi-EPC reads were skipped).

- **BUG-20 (support reply not working)** — diagnosed UI-side: the "Save Comment" form required a Ticket Status, but the status dropdown started empty → silent validation fail. (Backend auth is fine: the `sub ?? NameIdentifier` fallback mirrors the working `AuthenticationController`/`UserContextService`, email send is non-throwing.) Fix: `SupportQueryDetailPage` now defaults the comment status to the query's current status on load and keeps it selected after save.

- **BUG-23 (dashboard pie charts)** — Race-level chart existed but was fed the WRONG fields: backend returns `totalRegistered/totalFinishers/totalDnf/totalDns`, UI `DashboardStatsDto` reads `totalParticipants/totalStarted/totalFinished/totalDnfOrNotStarted` → existing race pie was rendering undefined. Fix: `DashboardService` now maps the raw backend shape (`totalStarted = registered − dns`, `totalDnfOrNotStarted = dnf + dns`) for BOTH event & race endpoints — repairs the race chart. Added new `EventStatsPanel.tsx` (cards Total/Started/Finished/DNF + progress pie Finished/Yet-to-Finish/DNF) mounted in `ViewEvent.tsx`. Note: backend has no first-class "Started"/in-progress field — values are derived from registered/finishers/dnf/dns.

- **TS errors fixed**: `ParticipantDetail.tsx:564` (used `checkpointName` in the success snackbar instead of leaving it unused); `RaceDashboard.tsx:218` (`(percent ?? 0)`).

- **Files modified — API**: `PublicResultsService.cs`, `DashboardService.cs`, `ResultsExportService.cs`, `RFIDImportService.cs`, `RFIDImportResponse.cs`, `ParticipantSearchReponse.cs`.
- **Files modified — UI**: public `EventResultsPage.tsx`/`GlobalResultsPage.tsx`/`LeaderboardPage.tsx`; admin `ParticipantDetail.tsx`/`ViewParticipants.tsx`/`SupportQueryDetailPage.tsx`/`races/RaceDashboard.tsx`/`events/ViewEvent.tsx`; `services/DashboardService.ts`; **new** `events/EventStatsPanel.tsx`.
- **No SQL scripts needed.** **Pending verify by user**: BUG-19 "X of Y" against a real multi-EPC file; BUG-23 event pie against an event with finishers; BUG-20 reply end-to-end.

### 2026-06-09 — FRONTEND REVIEW + VERIFY (Bug Fix Round 4 — UI repo @ C:\Projects\Runnatics.UI\Runnatics.Ui)

- **Result: PASS.** `npm run build` (vite) ✅ built in 12s. `npx tsc --noEmit` ✅ for all 4 reviewed files (2 pre-existing errors exist in UNRELATED files not touched this round: `ParticipantDetail.tsx:564` unused var, `RaceDashboard.tsx:218` possibly-undefined — flag to UI owner, not this round's scope).
- **✅ #4 rankBy mismatch is HANDLED — the backend "ChipTime"/"Chip time" inconsistency does NOT cause a silent failure.** All three pages read the **top-level** `data.rankBy` (the no-space `"ChipTime"`/`"GunTime"` form) and compare `rankBy === 'GunTime'`. The per-category spaced string (`"Chip time"`/`"Gun time"`) on `GroupedLeaderboardCategory.rankBy` is **never consumed** — each page derives its own label from the single top-level flag. Exact case match confirmed (BE emits `"GunTime"`, FE checks `'GunTime'`). All three default to `'ChipTime'` if absent. So the mismatch I flagged from the backend is inert here — but it stays a latent trap if anyone later wires a component to `category.rankBy`.
- **✅ #1 Race tabs**: `getGroupedLeaderboard(eventId, selectedRaceId, …)` is keyed by encrypted RaceId; refetch dep `[eventId, selectedRaceId, debouncedSearch]`. Defaults to first race via `useEffect([ev?.encryptedId]) → setSelectedRaceId(races[0]?.encryptedRaceId ?? ')`. Tab click resets page+search. `races` falls back from `ev.races` to `ev.categories`.
- **✅ #2 Overall section**: "Overall Result" (line 444) renders ABOVE "Age Category Result" (line 480). `OverallTable` shows a Gender column with `<GenderBadge>` (F/female→pink "F", else blue "M") — matches BE "M"/"F". `isGunTime` drives both the column header and the cell (`isGunTime ? p.gunTime : p.chipTime`).
- **✅ #3 Age Category**: both Male and Female columns are always rendered in the grid; an empty column shows `"No results."` (GenderColumn line 154-155) rather than being hidden.
- **✅ #5 Pagination**: client-side `PAGE_SIZE=50` over `tableRows`; `Pagination` hides when `totalPages<=1`; page resets on race/search change. Works.
- **🟠 EDGE-CASE BUG found (EventResultsPage Overall section, lines 310-314, 463-475)**: `podium = overallResults.slice(0,3)` and `tableRows = overallResults.slice(3)` — the table deliberately drops the top 3 (shown only on the podium). But `showPodium` requires `podium.length >= 3`. Consequences for small races:
  - **<3 finishers**: podium hidden (`length<3`) AND `tableRows` is empty → the Overall section renders **"No results available yet"** even though 1-2 finishers exist (they still appear in Age Category). Real, though only for tiny fields.
  - **exactly 3 finishers**: podium renders, but `pagedRows` is empty so a spurious **"No results available yet"** message also renders directly below the podium (cosmetic).
  - Fix suggestion: only carve the podium out of the table when `overallResults.length > 3` (e.g. `const usePodium = overallResults.length > 3; const tableRows = usePodium ? overallResults.slice(3) : overallResults;`), and gate the empty-state on `overallResults.length === 0`. Not a blocker for typical Racetik races.
- **GlobalResultsPage / LeaderboardPage**: identical, correct rankBy plumbing — `CategoryTable`/`CategorySection` take a `rankBy` prop from top-level `data.rankBy`, default `'ChipTime'`, label + time cell switch on `=== 'GunTime'`. No Overall-section podium logic there, so the edge-case bug above does not apply to them.
- **Note**: this review ran against the UI repo directly from the API session (file tools are path-agnostic) — no separate `claude` session was needed.

### 2026-06-09 — REVIEW + VERIFY findings (Bug Fix Round 4 — BUG-07 fe, BUG-08, BUG-10, BUG-13)

- **Result: backend PASS · frontend UNVERIFIABLE.** `dotnet build` ✅ 0 errors (13 pre-existing warnings). `npm run build` NOT RUN — the UI repo is not in this workspace (confirmed: no `src/main`, no `*.tsx`, no `publicApi.ts`; CONTEXT.md already notes "UI repo not present in this workspace"). The 4 frontend files in the review brief (`LeaderboardPage.tsx`, `GlobalResultsPage.tsx`, `EventResultsPage.tsx`, `publicApi.ts`) do not exist here, so BUG-07 race-tabs / Overall+M-F badges / Age-Category-below / female "No results", and the LeaderboardPage/GlobalResultsPage rankBy display fix CANNOT be reviewed from this repo.
- **Only 2 files actually changed this round** (`git status`): `PublicLeaderboardEntryDto.cs` (+`Gender`) and `PublicResultsService.cs` (showAll pageSize + Gender projection). Both changes are confined to `GetPublicGroupedLeaderboardAsync`.
- **✅ Gender projection correct (concern #5)**: `OverallResults[].Gender = r.Participant.Gender` returns **"M"/"F"**, NOT "male"/"female". Confirmed via `ParticipantConfiguration.GenderNormalizer` — write-side normalizes M/MALE→"M", F/FEMALE→"F"; read-side is identity (`v => v`). Consistent with the existing in-memory `"M"=>"Male"` switch in the grouped view (which proves materialized value is "M"/"F", not "Male").
  - **🟡 Caveat (pre-existing data, not introduced)**: the read converter is identity, so any LEGACY row written before the normalizer existed could still hold "Male"/"Female" in the DB column and would project verbatim into `Gender` — a frontend M/F badge would then receive "Male". Grouped view tolerates this (switch `var g => g` passthrough); the flat OverallResults badge would not. Only a risk if un-normalized historical data exists.
- **🟠 FINDING — silent truncation >1000 finishers (concern #6)**: when `showAll=true`, `pageSize=1000` and `page=1` are FORCED. `OverallResults` does `.Skip(0).Take(1000)`, so a race with >1000 finishers silently drops entries 1001+ from the Overall section, and paging is disabled (page pinned to 1). `TotalOverall`/`TotalFinishers` still report the true count, so the UI shows "N finishers" but lists at most 1000. Only documented in a code comment ("up to 1000"); no API-level signal of truncation. **Age Category section is NOT affected** — `grouped` is built from the full `allFinishers` list, not the paginated slice. Acceptable for current event sizes but should be logged/flagged if events can exceed 1000.
- **✅ No regressions (concern #7)**: `Gender` DTO addition is purely additive (nullable, default null) — no existing consumer breaks; build confirms. Only `GetPublicGroupedLeaderboardAsync` touched; `GetPublicResultsAsync`, `GetPublicResultByBibAsync`, podium build, and admin paths untouched. The removed top-of-method `page`/`pageSize` locals were relocated (not deleted) below the `topN` calc with identical clamping for the non-showAll path → behavior unchanged for regular browsing.
- **🟡 Minor (pre-existing) — RankBy string inconsistency**: top-level `RankBy` = `"ChipTime"`/`"GunTime"` (no space) while per-category `RankBy` = `"Chip time"`/`"Gun time"` (spaced, capitalized differently). If the frontend rankBy fix string-matches either, ensure it reads the right one. Not introduced this round.
- **Backend BUG-08/BUG-10/BUG-13**: no backend files for these in this round's diff — they are frontend-only (DTO additions aside). Cannot verify from this repo.
- **No files modified during review** (CLAUDE.md Rule 4).

### 2026-04-15 — backend-agent — Testing Feedback: Event/Participant/Race Fixes

- **What was built**: 5 feedback items addressed from testing
- **Files modified**:
  - `Runnatics.Models.Client/Requests/Events/EventRequest.cs` — removed `TimeZone` and `Status` fields (set server-side)
  - `Runnatics.Services/EventsService.cs` — `CreateEventEntity` now sets `Status = Draft`, `TimeZone = "Asia/Kolkata"` server-side
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` — ignore Status/TimeZone/MaxParticipants/RegistrationDeadline on EventRequest→Event; ignore TotalParticipants/EncodedEpcCount on Race→RaceResponse
  - `Runnatics.Models.Data/Entities/Participant.cs` — added `ManualDistance` (decimal?) and `LoopCount` (int?)
  - `Runnatics.Models.Data/Entities/Results.cs` — added `ManualFinishTimeMs` (long?) for admin-entered finish time
  - `Runnatics.Models.Client/Responses/Races/RaceResponse.cs` — added `TotalParticipants` and `EncodedEpcCount`
  - `Runnatics.Models.Client/Responses/Participants/ParticipantSearchReponse.cs` — added `List<CheckpointTimeDto>? Checkpoints`
  - `Runnatics.Services.Interface/IParticipantImportService.cs` — added `UpdateParticipantExtendedAsync` and `DeleteParticipantAsync`
  - `Runnatics.Services/ParticipantImportService.cs` — implemented new methods; `PopulateCheckpointTimesAsync` now also builds `Checkpoints` list
  - `Runnatics.Services/RaceService.cs` — `LoadRaceResponsesAsync` now computes TotalParticipants and EncodedEpcCount via two GROUP BY queries
  - `Runnatics.Api/Controller/ParticipantsController.cs` — added `PUT ~/api/races/{raceId}/participants/{participantId}` and `DELETE ~/api/races/{raceId}/participants/{participantId}`
- **Files created**:
  - `Runnatics.Models.Client/Responses/Participants/CheckpointTimeDto.cs` — structured checkpoint time DTO
  - `Runnatics.Models.Client/Requests/Participant/UpdateParticipantRequest.cs` — extended update DTO with RunStatus/DisqualificationReason/ManualTime/ManualDistance/LoopCount/RaceId
  - `db/scripts/Participants_AddManualFields_20260415.sql` — ALTER TABLE scripts for new columns
- **Decisions made**:
  - Location fields (VenueName, City, Country) were already nullable/optional in EventRequest — no change needed
  - RunStatus "OK" maps to Participant.Status = "Registered" (or "Finished" in Results); other values pass through
  - Race reassignment in UpdateParticipantExtended soft-deletes the old record and creates a new one in the target race
  - Checkpoint times in participant search now return both `CheckpointTimes` (dictionary, backward-compat) and `Checkpoints` (ordered list)
  - EPC count uses ChipAssignment → Participant join since ChipAssignment has no direct RaceId
- **Pending**: Run `db/scripts/Participants_AddManualFields_20260415.sql` against Azure SQL to add the new columns

### 2026-04-08 — backend-agent — Generate & Download Participant Certificate

- **What was built**: `GET /api/certificates/participant/{participantId}/download` — generates a filled PNG certificate for a participant using SkiaSharp
- **Files modified**:
  - `Runnatics.Services/Runnatics.Services.csproj` — added SkiaSharp 2.88.8 + SkiaSharp.NativeAssets.Linux 2.88.8
  - `Runnatics.Services.Interface/ICertificatesService.cs` — added `GenerateParticipantCertificateAsync`
  - `Runnatics.Services/CertificatesService.cs` — added IHttpClientFactory to constructor; new public method + 6 private helpers
  - `Runnatics.Api/Controller/CertificatesController.cs` — added `DownloadParticipantCertificate` action
- **Decisions made**:
  - Template selection: race-specific → IsDefault → event-wide (RaceId = null) — mirrors `GetTemplateByRaceAsync`
  - `Results.FinishTime` (ms) → ChipTime; `Results.GunTime` (ms) → GunTime; formatted as `HH:MM:SS` via TotalHours
  - `RaceCategory` = `Race.Title`; `Category` = `Participant.AgeCategory`
  - `Photo` field skipped — no photo property on `Participant`; `CustomText` renders `field.Content` verbatim
  - Background: base64 `BackgroundImageData` preferred over URL (fetched via IHttpClientFactory)
  - All IDs accept encrypted strings via existing `TryParseOrDecrypt`
- **Pending**: Photo field support requires adding a photo URL property to the Participant entity
### 2026-03-31 — backend-agent — SupportQuery / Contact Us Feature

- **Branch**: `feature/OnlineReadingsFlow` (existing branch)
- **What was built**: Full support query feature — public Contact Us submission, admin list/detail/update/comment/email/delete endpoints
- **Files created**:
  - `db/scripts/SupportQuery_CreateTables_20260331.sql` — 4 tables + status seed data
  - `Runnatics.Models.Data/Entities/SupportQueryStatus.cs`
  - `Runnatics.Models.Data/Entities/SupportQueryType.cs`
  - `Runnatics.Models.Data/Entities/SupportQuery.cs`
  - `Runnatics.Models.Data/Entities/SupportQueryComment.cs`
  - `Runnatics.Data.EF/Config/SupportQueryStatusConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryTypeConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryCommentConfiguration.cs`
  - `Runnatics.Models.Client/Requests/Support/ContactUsRequestDto.cs`
  - `Runnatics.Models.Client/Requests/Support/AddCommentRequestDto.cs`
  - `Runnatics.Models.Client/Requests/Support/UpdateQueryRequestDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryListItemDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryDetailDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryCommentDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryCountsDto.cs`
  - `Runnatics.Services.Interface/ISupportQueryService.cs`
  - `Runnatics.Services/SupportQueryService.cs`
  - `Runnatics.Api/Controller/SupportQueryController.cs`
- **Files modified**:
  - `Runnatics.Data.EF/RaceSyncDbContext.cs` — added 4 DbSets + 4 ApplyConfiguration calls
  - `Runnatics.Services.Interface/IEmailService.cs` — added `SendAsync(string to, string subject, string body)`
  - `Runnatics.Api/Program.cs` — registered `ISupportQueryService → SupportQueryService`
- **Decisions made**:
  - SupportQuery/SupportQueryComment entities do NOT use AuditProperties owned type — these are support tickets with a simpler schema (CreatedAt/UpdatedAt directly on entity), as per explicit SQL spec
  - `AssignedToUserId = 0` in UpdateQueryRequestDto is treated as "unassign" (sets to null)
  - `LastUpdated` relative label is computed in service layer (days → hours → minutes)
  - `DeleteCommentAsync` is a hard delete (uses `repo.DeleteAsync(id)`) since comments have no AuditProperties
  - Admin user ID in AddComment is extracted from JWT `sub` claim in the controller
- **Pending**: IEmailService `SendAsync` implementation needs to be added to the concrete email service class

### 2026-04-16 — backend-agent — Public API: DTOs (Prompt 1)

- **What was built**: Public-facing DTO layer for the Runnatics marketing website
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicEventSummaryDto.cs` — summary card for event listings
  - `Runnatics.Models.Client/Public/PublicEventDetailDto.cs` — extends summary; adds Races, FullDescription, Schedule, RouteMapUrl, RegistrationDeadline, ContactEmail
  - `Runnatics.Models.Client/Public/PublicRaceCategoryDto.cs` — race info for public display; Price is null (Race entity has no Price column yet)
  - `Runnatics.Models.Client/Public/PublicResultDto.cs` — race result with splits; GunTime/NetTime are TimeSpan? converted from milliseconds
  - `Runnatics.Models.Client/Public/PublicSplitDto.cs` — checkpoint split time; CheckpointName from SplitTimes.ToCheckpoint.Name
  - `Runnatics.Models.Client/Public/PublicGalleryImageDto.cs` — gallery image placeholder (no GalleryImage entity yet)
  - `Runnatics.Models.Client/Public/PublicPagedResultDto.cs` — paged wrapper with TotalPages/HasNext/HasPrevious computed props
  - `Runnatics.Models.Client/Requests/Public/PublicContactRequestDto.cs` — contact form with DataAnnotations
- **Decisions made**:
  - `Event.Slug` exists — no workaround needed
  - `PagingList<T>` only has TotalCount (extends List<T>) — created new `PublicPagedResultDto<T>` with full pagination metadata
  - No encrypted IDs on public DTOs (plain int/slug — public data, no security concern)
  - `Race.Price` does not exist — property left nullable with comment; add column when ready
  - `PublicEventDetailDto` inherits `PublicEventSummaryDto` to avoid duplication
- **Pending**: Prompts 3–5 (controller, CORS, verify/build)

### 2026-04-16 — backend-agent — Public API: Service Methods (Prompt 2)

- **What was added**: New methods to existing service interfaces/implementations only (no new classes)
- **Files modified**:
  - `Runnatics.Services.Interface/IEventsService.cs` — added `GetPublicEventsAsync(bool isPast, string? city, string? searchQuery, int page, int pageSize)` and `GetPublicEventBySlugAsync(string slug)`; alias `DataPagingList` avoids collision with client `PagingList<T>`
  - `Runnatics.Services/EventsService.cs` — implemented both methods in `#region Public (no-auth) methods`; list uses filtered `.Include(e => e.Races)`, detail uses `.ThenInclude(r => r.Participants)` for per-race counts
  - `Runnatics.Services.Interface/IResultsService.cs` — added `GetPublicResultsAsync(int eventId, string? raceName, string? searchQuery, string? gender, int page, int pageSize)` returning `DataResultsPagingList`
  - `Runnatics.Services/ResultsService.cs` — implemented `GetPublicResultsAsync`; filters by eventId, race name, bib/name search, gender; includes `Participant`, `Race`, `Participant.SplitTimes → ToCheckpoint`
  - `Runnatics.Services.Interface/ISupportQueryService.cs` — added `CreatePublicQueryAsync(string name, string email, string? phone, string subject, string message, string? eventName)`
  - `Runnatics.Services/SupportQueryService.cs` — implemented `CreatePublicQueryAsync`; embeds Name/Phone/EventName into the Body (no schema change needed)
- **Decisions made**:
  - All existing event search methods require `_userContext.TenantId` — unusable for public; new methods are tenant-agnostic
  - `GetEventById` requires encrypted ID + tenant scope — slug-based lookup is a new method
  - `GetLeaderboardAsync` returns admin leaderboard format — not suitable; new `GetPublicResultsAsync` is paged/filterable
  - `SubmitQueryAsync` only accepts Subject/Body/SubmitterEmail; `CreatePublicQueryAsync` packs Name/Phone/EventName into the Body string since `SupportQuery` has no separate columns for them
  - `EventOrganizer` has no email field → `ContactEmail` in `PublicEventDetailDto` will remain null
  - EF Core filtered includes (`.Where()` inside `.Include()`) used for Races and Participants to honour soft-delete
  - `DataPagingList` / `DataResultsPagingList` type aliases in interface files prevent CS0104 ambiguity with same-named types in Models.Client

### 2026-04-23 — backend-agent — Racetik API tasks (API-1 through API-11)

- **What was built**: 9 API tasks from the Racetik feature spec
- **Files modified**:
  - `Runnatics.Models.Client/Requests/Events/EventSettings.cs` — removed `RemoveBanner`, `ShowResultSummaryForRaces`, `UseOldData`, `AllowParticipantEdit` from `EventSettingsRequest` (hardcoded server-side)
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` — ignore the 4 removed EventSettings fields in mapper; ignore `BannerImage`/`BannerContentType` in `EventRequest → Event`; map `BannerImage → BannerBase64` in `Event → EventResponse`
  - `Runnatics.Services/EventsService.cs` — hardcode 4 fields to `false` in `CreateEventSettings`, `SaveEventAsync`, and `UpdateEventSettings`; add banner save on create; add banner existence check on update; update `GetPublicEventsAsync` to require `ConfirmedEvent = true` AND `Published = true`
  - `Runnatics.Models.Client/Requests/Events/EventRequest.cs` — added `BannerBase64` property
  - `Runnatics.Models.Client/Responses/Events/EventResponse.cs` — added `BannerBase64` property
  - `Runnatics.Models.Client/Responses/Participants/ParticipantSearchReponse.cs` — added `IsEpcMapped` (bool)
  - `Runnatics.Services/ParticipantImportService.cs` — set `IsEpcMapped` in `PopulateCheckpointTimesAsync`; handle `DateOfBirth` in `UpdateParticipantExtendedAsync`; add `ManualCheckpointTimes` handling (creates SplitTimes records, sets `IsManualTiming = true`); added `ExportParticipantsAsync` (xlsx via ClosedXML)
  - `Runnatics.Models.Client/Requests/Participant/UpdateParticipantRequest.cs` — added `DateOfBirth`, `ManualCheckpointTimes` (list of `ManualCheckpointTime`)
  - `Runnatics.Models.Data/Entities/Participant.cs` — added `IsManualTiming` (bool, default false)
  - `Runnatics.Services.Interface/IParticipantImportService.cs` — added `ExportParticipantsAsync`
  - `Runnatics.Api/Controller/ParticipantsController.cs` — added `GET ~/api/races/{raceId}/participants/export`
  - `Runnatics.Services.Interface/IBibMappingService.cs` — added `GetParticipantsWithMappingStatusAsync`
  - `Runnatics.Services/BibMappingService.cs` — implemented `GetParticipantsWithMappingStatusAsync` (paginated, search by name/BIB, filter by mapped/unmapped)
  - `Runnatics.Api/Controller/BibMappingsController.cs` — added `GET /api/bib-mappings/participants` endpoint
  - `Runnatics.Services/RfidReaderService.cs` — added 500ms RSSI debounce: buffers reads per EPC, broadcasts best RSSI per EPC after window expires
  - `Runnatics.Services/Runnatics.Services.csproj` — added ClosedXML 0.102.3
- **Files created**:
  - `Runnatics.Models.Client/Requests/BibMapping/GetEpcMappingRequest.cs` — pagination/filter request for EPC mapping
  - `Runnatics.Models.Client/Responses/BibMapping/BibMappingParticipantResponse.cs` — paginated participant+mapping response
  - `db/scripts/Participants_AddIsManualTiming_20260423.sql` — ALTER TABLE for new `IsManualTiming` column
- **Decisions made**:
  - API-2 (location fields optional): Already done — all fields are `string?` with no `[Required]` in `EventRequest`
  - API-5 (participant search): Already working — `ParticipantImportService.Search` correctly applies all filters simultaneously
  - API-10 (EPC uniqueness + override): Already implemented in `BibMappingService.CreateAsync` with full conflict handling
  - `BannerImage` on the `Event` entity is the base64 storage field — `BannerBase64` in DTOs maps to it
  - Manual checkpoint times use SplitTimes entity; `SplitTimeMs` is relative to earliest known reading, or 0 if unknown
  - RSSI debounce is per-EPC (not per-window) — multiple EPCs in the same window are each deduplicated independently
- **Pending**: Run `db/scripts/Participants_AddIsManualTiming_20260423.sql` against Azure SQL

### 2026-04-17 — backend-agent — Bug Fixes (testing-feedback)

- **Bug #7 (Critical) — Bulk upload data loss**: Root cause was operator precedence bug in `ParticipantImportService.ProcessStagingDataAsync` line 258-266. `raceId.HasValue ? ... : true && IsActive && !IsDeleted` skipped soft-delete filters when raceId was provided, causing deleted participants' bibs to be treated as duplicates. Fix: `(!raceId.HasValue || p.RaceId == raceId.Value)`. Also: invalid staging records now marked "Invalid" (not left as "Pending"), and ProcessImportResponse.Errors list now populated with per-row details.
- **Bug #12 — Race category change response empty**: `UpdateParticipantExtendedAsync` returned `Task` (void), controller returned `{ }`. Changed return type to `Task<ParticipantSearchReponse?>`, added `MapToSearchResponse` helper, controller now returns full participant data (Bib, Name, Gender, Phone, Email, AgeCategory, Status).
- **Bug #11 — Export endpoint missing**: No export endpoint existed. Created `GET /api/results/{eventId}/{raceId}/export` on ResultsController. Returns CSV with: BibNumber, Name, Email, Mobile, Gender, AgeCategory, Status, GunTime, ChipTime, OverallRank, GenderRank, CategoryRank, plus dynamic columns for each checkpoint's split time. Added Email/Phone to LeaderboardEntry DTO and AutoMapper mapping.
- **Bug #1 — Event edit past dates**: No past-date validation exists in code. ValidateEventRequest only checks for null. No fix needed — issue likely elsewhere (frontend or DB constraint).
- **Bug #4 — Location fields optional**: Fields (VenueName, City, Country) are already `string?` without [Required]. No fix needed.
- **Bug #10 — Checkpoint clone**: Endpoint exists at `POST {eventId}/{sourceRaceId}/{destinationRaceId}/clone`. Service logic looks correct. Issue likely frontend-side (routing/params).
- **Files modified**: ParticipantImportService.cs, IParticipantImportService.cs, ParticipantsController.cs, ResultsController.cs, AutoMapperMappingProfile.cs, LeaderboardEntry.cs

### 2026-05-02 — backend-agent — Manual Time Entry with Race Recalculation

- **What was built**: `POST /api/RFID/{eventId}/{raceId}/participant/{participantId}/manual-time` — records a manual finish time for a participant, then recalculates the full race ranking
- **Files created**:
  - `Runnatics.Models.Client/Requests/RFID/ManualTimeRequest.cs` — `{ FinishTimeMs: long }` body DTO
  - `Runnatics.Models.Client/Responses/RFID/ManualTimeResponse.cs` — returns updated rank, bib, formatted time, total finishers
- **Files modified**:
  - `Runnatics.Services.Interface/IResultsService.cs` — added `RecordManualTimeAsync(eventId, raceId, participantId, finishTimeMs)`
  - `Runnatics.Services/ResultsService.cs` — implemented `RecordManualTimeAsync`: upserts Results record (ManualFinishTimeMs + FinishTime/GunTime/NetTime), sets `Participant.IsManualTiming = true`, calls private `CalculateResultRankingsAsync` to re-rank ALL finishers in the race
  - `Runnatics.Api/Controller/RFIDController.cs` — injected `IResultsService`; added the POST endpoint
- **Decisions made**:
  - Upsert strategy: if a Results row exists (e.g., prior DNF), it is updated in-place; otherwise a new row is created — avoids wipeout of other participants' results
  - Only `CalculateResultRankingsAsync` is called (not the full `CalculateResultsAsync`), so existing RFID-derived finish times are preserved; rankings are simply recomputed across all Finished results
  - `Results.ManualFinishTimeMs` stores the raw admin entry; `FinishTime`/`GunTime`/`NetTime` are all set to the same value (no gun-to-chip offset available for manual entry)
  - `Participant.IsManualTiming = true` is set so the UI can distinguish chip vs. manual finishers

### 2026-05-05 — backend-agent — PublicController CLAUDE.md Compliance Fix

- **What was fixed**: Refactored `PublicController` to comply with all CLAUDE.md rules (Rule 2: thin controller only)
- **Violations removed**:
  - Entity `using` aliases (`Event`, `Results`) — controller no longer touches domain entities
  - All private mapping helpers (`MapToSummary`, `MapToDetail`, `MapToResultDto`, `GetBannerBase64`) — moved to service layer
  - In-memory year filter in `GetEvents` — moved to `GetPublicEventsAsync` as a DB-side filter
  - Multiple service calls per action (`GetEventResults`: 3 calls, `GetResultByBib`: 2 calls, `GetPublicStats`: 2 calls) — consolidated into single service calls
  - Business logic in controller (publish gate, DNF filter, bib match, stats arithmetic) — moved to service layer
- **Files modified**:
  - `Runnatics.Models.Client/Public/PublicStatsDto.cs` — new DTO for stats endpoint
  - `Runnatics.Services.Interface/IEventsService.cs` — `GetPublicEventsAsync` now returns `PublicPagedResultDto<PublicEventSummaryDto>` + `year` param; `GetPublicEventBySlugAsync` returns `PublicEventDetailDto?`; added `GetPublicStatsAsync`
  - `Runnatics.Services/EventsService.cs` — implemented updated signatures; added `MapToEventSummaryDto`, `MapToEventDetailDto`, `GetEventBannerBase64` private helpers; implemented `GetPublicStatsAsync`
  - `Runnatics.Services.Interface/IPublicResultsService.cs` — removed `GetPublicResultsAsync` and `GetEffectivePublicLeaderboardSettingsAsync` (now private); added `GetPublicEventResultsAsync` and `GetPublicResultByBibAsync`
  - `Runnatics.Services/PublicResultsService.cs` — `GetPublicResultsAsync` and `GetEffectivePublicLeaderboardSettingsAsync` made private; added `GetPublicEventResultsAsync`, `GetPublicResultByBibAsync`, `MapToResultDto` private static helper
  - `Runnatics.Api/Controller/PublicController.cs` — all actions now call exactly ONE service method; no entity types, no mapping, no business logic

### 2026-05-05 — backend-agent — SRP Refactoring of Public Results Changes

- **What was built**: Applied Single Responsibility Principle to the 2026-05-05 changes
- **Files modified**:
  - `Runnatics.Models.Client/Public/PublicGroupedLeaderboardDto.cs` — now contains only `PublicGroupedLeaderboardDto`
  - `Runnatics.Models.Client/Public/PublicParticipantDetailDto.cs` — now contains only `PublicParticipantDetailDto`
  - `Runnatics.Services.Interface/IResultsService.cs` — removed 4 public no-auth methods (`GetPublicResultsAsync`, `GetEffectivePublicLeaderboardSettingsAsync`, `GetPublicGroupedLeaderboardAsync`, `GetPublicParticipantDetailAsync`)
  - `Runnatics.Services/ResultsService.cs` — removed the same 4 methods; now admin-only
  - `Runnatics.Api/Controller/PublicController.cs` — now injects `IPublicResultsService` instead of `IResultsService`
  - `Runnatics.Api/Program.cs` — registered `IPublicResultsService → PublicResultsService`
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicGenderGroupDto.cs`
  - `Runnatics.Models.Client/Public/PublicCategoryGroupDto.cs`
  - `Runnatics.Models.Client/Public/PublicLeaderboardEntryDto.cs`
  - `Runnatics.Models.Client/Public/PublicParticipantInfoDto.cs`
  - `Runnatics.Models.Client/Public/PublicTimeDetailDto.cs`
  - `Runnatics.Models.Client/Public/PublicSplitDetailDto.cs`
  - `Runnatics.Services.Interface/IPublicResultsService.cs`
  - `Runnatics.Services/PublicResultsService.cs`
- **Decisions made**:
  - `IResultsService` is now admin-only; `IPublicResultsService` owns all anonymous public endpoints
  - `PublicResultsService` depends only on `IUnitOfWork`, `IEncryptionService`, `ILogger` — no `IMapper` or `IUserContextService` needed
  - One class per .cs file rule enforced across all 8 new DTO/service files

### 2026-05-05 — backend-agent — Public API Security (Rate Limiting + CORS + X-Public-Key)

- **What was built**: 3-layer security for `/api/public/*` endpoints
- **Files modified**:
  - `Runnatics.Api/Program.cs` — rate limiting changed from global to per-IP partitioned (`AddPolicy<string>` with `RateLimitPartition.GetSlidingWindowLimiter`); `PublicRead` now 60 req/min per IP, `PublicWrite` now 5 req/10 min per IP; added inline `X-Public-Key` middleware that short-circuits with 401 for requests to `/api/public/*` missing or with wrong key
  - `Runnatics.Api/appsettings.json` — added `PublicApi:Key = "SET_IN_AZURE_ENV_VARS"` (real value must be set as Azure App Service environment variable)
- **CORS**: `PublicSite` policy was already correct (explicit `racetik.com`/`www.racetik.com` origins, no `AllowAnyOrigin`) — no changes needed
- **Decisions made**:
  - X-Public-Key middleware placed between `UseRouting()` and `UseCors()` so it fires before auth and before route matching overhead
  - Rate limiting uses `RateLimitPartition` keyed on `RemoteIpAddress` — each IP gets its own counter, not a shared global counter
- **Pending**:
  - Set `PublicApi__Key` environment variable in Azure App Service (override the placeholder)
  - UI: add `'X-Public-Key': import.meta.env.VITE_PUBLIC_API_KEY` to the publicApi.ts fetch helper
  - UI: add `VITE_PUBLIC_API_KEY=` to the `.env.example` file (UI repo not present in this workspace)

### 2026-05-05 — backend-agent — Excel Export Fix + Public Leaderboard + Public Participant Detail

- **What was built**: 3 features — fixed admin Excel export (Task 1), added public grouped leaderboard endpoint (Task 2), added public participant detail endpoint (Task 3)
- **Files modified**:
  - `Runnatics.Services/ResultsExportService.cs` — rewritten to bypass `GetLeaderboardAsync` entirely; now injects `RaceSyncDbContext` + `IEncryptionService` and queries Results directly (no leaderboard visibility gates). Builds 2-sheet Excel: "Overall Results" (all results with optional splits/pace columns from leaderboard settings) and "Category Results" (grouped by gender→category with merged group header rows)
  - `Runnatics.Services.Interface/IResultsService.cs` — added `GetPublicGroupedLeaderboardAsync` and `GetPublicParticipantDetailAsync`
  - `Runnatics.Services/ResultsService.cs` — implemented both new methods in `#region Public (no-auth) methods`
  - `Runnatics.Api/Controller/PublicController.cs` — added `GET api/public/{eventId}/{raceId}/leaderboard` and `GET api/public/participant/{participantId}`
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicGroupedLeaderboardDto.cs` — 4 DTOs: `PublicGroupedLeaderboardDto`, `PublicGenderGroupDto`, `PublicCategoryGroupDto`, `PublicLeaderboardEntryDto`
  - `Runnatics.Models.Client/Public/PublicParticipantDetailDto.cs` — 4 DTOs: `PublicParticipantDetailDto`, `PublicParticipantInfoDto`, `PublicTimeDetailDto`, `PublicSplitDetailDto`
- **Decisions made**:
  - Excel export: bypasses leaderboard visibility pipeline entirely — admin always sees all results regardless of MaxDisplayedRecords/NumberOfResultsToShowOverall
  - Column control for export still honors leaderboard settings (ShowPace, ShowSplitTimes, ShowGenderResults, etc.)
  - Public grouped leaderboard: default shows top 3 per category (or `NumberOfResultsToShowCategory` from settings); `showAll=true` returns all
  - Participant detail URL format: `/p/{encryptedParticipantId}` built in service layer
  - `GetPublicGroupedLeaderboardAsync` accepts encrypted IDs (same as admin endpoints)
  - `GetPublicParticipantDetailAsync` accepts encrypted participantId
  - `ResultsExportService` no longer depends on `IResultsService` — removed that dependency, replaced with `RaceSyncDbContext` + `IEncryptionService`

### 2026-05-10 — backend-agent — Race Notification System (Option B)

- **What was built**: Race SMS/Email notification layer using MSG91 (Flow API) + Mailer91 — separate from auth SMTP path
- **Files created**:
  - `Runnatics.Models.Client/Notifications/NotificationResult.cs` — result DTO with Ok/Fail factory methods
  - `Runnatics.Services.Interface/INotificationSmsService.cs` — checkpoint + completion SMS interface
  - `Runnatics.Services.Interface/INotificationEmailService.cs` — completion + support ticket email interface
  - `Runnatics.Services.Interface/IRaceNotificationService.cs` — orchestrator interface
  - `Runnatics.Services/Config/Msg91Config.cs` — bound to `Notification:Msg91` config section
  - `Runnatics.Services/Config/Mailer91Config.cs` — bound to `Notification:Mailer91` config section
  - `Runnatics.Services/Msg91NotificationSmsService.cs` — MSG91 Flow API; CompletionTemplateId = 69e08448cd4818fe270e6b32
  - `Runnatics.Services/Mailer91NotificationEmailService.cs` — Mailer91 HTTP API; RaceCompletion + SupportTicket HTML templates
  - `Runnatics.Services/RaceNotificationService.cs` — orchestrator; loads participant/result/query from DB; logs to NotificationLogs
  - `Runnatics.Models.Data/Entities/NotificationLog.cs` — append-only log entity (no AuditProperties)
  - `Runnatics.Data.EF/Config/NotificationLogConfiguration.cs` — Fluent API config
  - `db/scripts/NotificationLog_CreateTable_20260510.sql` — CREATE TABLE + index script
- **Files modified**:
  - `Runnatics.Api/appsettings.json` — added `Notification:Msg91` and `Notification:Mailer91` sections (keys SET_IN_AZURE_ENV_VARS)
  - `Runnatics.Data.EF/RaceSyncDbContext.cs` — added `NotificationLogs` DbSet + `NotificationLogConfiguration` apply
  - `Runnatics.Api/Program.cs` — registered `IOptions<Msg91Config>`, `IOptions<Mailer91Config>`, `INotificationSmsService`, `INotificationEmailService`, `IRaceNotificationService` with typed HttpClients
  - `Runnatics.Services/SupportQueryService.cs` — injected `IRaceNotificationService`; replaced `SendSubmissionConfirmationAsync` (SMTP) with `NotifySupportTicketCreatedAsync` (Mailer91) in both `SubmitQueryAsync` and `CreatePublicQueryAsync`
  - `Runnatics.Services/ResultsService.cs` — injected `IRaceNotificationService`; fire-and-forget `NotifyRaceCompletionAsync` after `CalculateResultRankingsAsync` in `RecordManualTimeAsync`
  - `Runnatics.Services/OnlineTagIngestionService.cs` — injected `IRaceNotificationService`; fire-and-forget `NotifyCheckpointCrossingAsync` per unique participant after SignalR push in `PushLiveCrossingEvents`
- **Decisions made**:
  - `ISmsService` / `IEmailService` (auth SMTP path) completely untouched
  - Checkpoint notification dedup: `RaceNotificationService` queries `NotificationLogs` for a successful SMS to same participant+race within 30s before sending (matches the RFID dedup window)
  - All notification calls are fire-and-forget (`Task.Run`) to keep RFID webhook and manual time endpoints fast
  - `Participant.Phone` (not Mobile) is the phone field
  - `IGenericRepository<T>.GetQuery(filter)` is the correct method — not `GetQueryable()`
  - `SupportQueryService` still keeps `_emailService` (used for admin reply emails in `SendCommentEmailAsync`)
- **Pending**:
  - Run `db/scripts/NotificationLog_CreateTable_20260510.sql` against Azure SQL
  - Set `Notification__Msg91__AuthKey`, `Notification__Mailer91__ApiKey`, `Notification__Msg91__CheckpointTemplateId` in Azure App Service environment variables

### 2026-05-11 — backend-agent — Bug 8: Fix Split Times (SplitTime & CumulativeTime incorrect)

- **Root cause**: `PerformanceMetricsBuilder.ProcessSplitTime` fell back to `st.SplitTimeMs` (cumulative from gun start) when `st.SegmentTime == null`. For non-first checkpoints this produced the cumulative gun time instead of the segment interval, so 4.5km showed "00:14:28" (gun→4.5km) instead of "00:14:07" (start→4.5km). The UI then computed its own cumulative as a running sum of these wrong SplitTime values, giving "00:14:50".
- **Fix**: Added `previousSplitTimeMs` tracking in `BuildSplitTimesAndPerformance`; in `ProcessSplitTime`, when `SegmentTime == null`, derive segment as `SplitTimeMs[i] - SplitTimeMs[i-1]` (or `SplitTimeMs[0]` for the first row).
- **Files modified**:
  - `Runnatics.Services/Helpers/PerformanceMetricsBuilder.cs` — added `previousSplitTimeMs` parameter to `ProcessSplitTime`; replaced single-line `st.SegmentTime ?? st.SplitTimeMs` fallback with three-branch derivation
- **Decisions made**:
  - The fix is display-layer only (no DB changes needed); `SplitTimeMs` (cumulative) and `SegmentTime` columns in `SplitTimes` table remain as-is
  - `CumulativeTime` computation (`SplitTimeMs[i] - startGunTimeMs`) was already correct — no change
  - Pace/speed for segments are now also computed from the correctly-derived `segmentTimeMs`

### 2026-05-11 — backend-agent — Bug 6: Show ALL raw RFID readings for participant detail

- **What was built**: `RawRfidTagReadings` on participant detail now returns ALL raw detections (not just 4 normalized) with enriched fields for IsNormalized, IsDuplicate, GunTime, NetTime, CheckpointDistance, and device name.
- **Files created**:
  - `Runnatics.Models.Client/Responses/Participants/RfidRawReadingDto.cs` — new DTO with Id, LocalTime, Date, Checkpoint, CheckpointDistance, Device, DeviceId, GunTime, NetTime, ChipId, ProcessResult, IsManual, IsDuplicate, IsNormalized
- **Files modified**:
  - `Runnatics.Services/ResultsService.cs` — rewrote `LoadRawRfidReadingsAsync`: added `participantId` param, added `UploadBatch.ReaderDevice` include for friendly device name, ordered by `ReadTimeUtc`, built `normalizedByRawId` dictionary to resolve IsNormalized/GunTime/NetTime; now returns `List<RfidRawReadingDto>`
  - `Runnatics.Models.Client/Responses/Participants/ParticipantDetailsResponse.cs` — changed `RawRfidTagReadings` from `List<RawRfidTagReading>` to `List<RfidRawReadingDto>`
- **Decisions made**:
  - `RawRfidTagReading.cs` retained (not deleted) — legacy DTO kept to avoid breaking any other consumers
  - `RfidReadings` (normalized, List<RfidReadingDetail>) left untouched — UI can keep using it for the compact 4-row view; `RawRfidTagReadings` is the new full-detail source
  - `Device` field = `UploadBatch.ReaderDevice.Name` when available, fallback to `r.DeviceId` (MAC string)
  - `IsDuplicate` = `ProcessResult == "Duplicate" || DuplicateOfReadingId.HasValue` (belt-and-suspenders)
  - Readings without checkpoint assignment (94 unassigned) included; `Checkpoint`/`CheckpointDistance` are null for those rows
  - Ordering changed from `TimestampMs` to `ReadTimeUtc` for consistent chronological display

### 2026-05-11 — backend-agent — Bug 8 (Part 2): Fix SplitTime/CumulativeTime in Results.SplitTimeInfo

- **Root cause**: `GetParticipantSplitsAsync` in `ResultsService` set `SplitTime = FormatTime(SplitTimeMs)` — the cumulative gun-start time — instead of the segment interval. `Results.SplitTimeInfo` also had no `CumulativeTime` field.
- **Two separate `SplitTimeInfo` types exist in the codebase**:
  - `Runnatics.Models.Client.Responses.Participants.SplitTimeInfo` — used by participant detail (`PerformanceMetricsBuilder`, already fixed in earlier session)
  - `Runnatics.Models.Client.Responses.Results.SplitTimeInfo` — used by leaderboard/results (fixed in this session)
- **Files modified**:
  - `Runnatics.Models.Client/Responses/Results/SplitTimeInfo.cs` — added `CumulativeTimeMs` (long) and `CumulativeTime` (string); added inline comments clarifying each field's meaning
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` — added `Ignore()` for `CumulativeTimeMs` and `CumulativeTime`
  - `Runnatics.Services/ResultsService.cs` — rewrote `GetParticipantSplitsAsync` loop: `SplitTime` now uses `SegmentTime` (falls back to `SplitTimeMs` only when null); added `CumulativeTime` = start row uses its own `SplitTimeMs`, all others use `SplitTimeMs - startSplitTimeMs`
- **Decisions made**:
  - `SegmentTime` string field retained unchanged (same value as `SplitTime`) for backward compatibility
  - `SplitTimeMs` raw field retained so the UI can compute its own cumulative if needed

### 2026-05-11 — backend-agent — Fix Checkpoint Name "Unassigned" in RFID Raw Readings

- **Root cause**: `ReadingCheckpointAssignment` correctly links raw readings to checkpoints, but the assigned checkpoints are **child checkpoints** (IDs 287, 291, 293) with empty `Name`. The parent checkpoint (e.g., ID 267 "Finish") has the same `DistanceFromStart` but different `Device`. `LoadRawRfidReadingsAsync` displayed `"Unassigned"` because `cp.Name` was empty.
- **Fix**: Query all named checkpoints for the race keyed by `DistanceFromStart`, then resolve empty names at mapping time — if `cp.Name` is empty, look up the parent name from that dictionary. Format as `"Name (X km)"`.
- **Files modified**:
  - `Runnatics.Services/ResultsService.cs` — added `raceId` parameter to `LoadRawRfidReadingsAsync` (signature: `(chipEpc, participantId, raceId, eventId, eventTimeZone)`); added `namedByDistance` dictionary lookup; replaced `"Unassigned"` fallback with parent-name resolution; updated call site in `GetParticipantDetailsAsync` to pass `decryptedRaceId`
- **Decisions made**:
  - Parent name lookup uses `DistanceFromStart` as the key — avoids any device/ID coupling
  - Display format `"Name (X km)"` applied only when a name is resolved; null when truly unassigned
  - Query uses `AsNoTracking()` and filters `IsActive && !IsDeleted` on checkpoint rows

### 2026-05-15 — Testing Round 1 Bug Fixes (Session 1 — commit 43c005e)

Branch: `bugfix/testing-round-1`. Constraints: Do NOT execute SQL script, do NOT merge or push.

**BUG API-2 + API-11 (MultipleEPC + tag counts)**
- `RFIDImportService.ProcessRFIDImportAsync` Phase 1 and Phase 2 loops: skip readings with `IsMultipleEpc == true`
- `UploadRFIDFileEventLevelAsync`: removed duplicate FileHash check block; `TotalTagsInFile` = distinct non-MultipleEpc EPCs; `TagsProcessed = 0` initially, set to `successCount` at completion
- `UploadRFIDFileAsync`: added `batch.TagsProcessed = 0`
- `RfidRawReadingDto`: added `IsMultipleEpc` bool; set from `r.IsMultipleEpc` in `LoadRawRfidReadingsAsync`

**BUG API-3 (Manual time upsert fix)**
- `RecordManualTimeAsync`: split time computation now treats values < 86,400,000ms as elapsed ms from race start; larger values fall back to IST-from-midnight conversion (legacy)
- SplitTimes UPSERT: if no existing row, creates new; infers `FromCheckpointId` from checkpoint order (doesn't require pre-existing row)

**BUG API-6 (Race category change)**
- New `PUT /api/participants/{eventId}/{raceId}/{participantId}/race-category` endpoint
- `ChangeParticipantCategoryAsync` in ResultsService: updates `participant.AgeCategory`, triggers re-ranking
- `ChangeRaceCategoryRequest` DTO: `{ AgeCategory: string }`

**BUG API-7 (Process result)**
- New `POST /api/participants/{eventId}/{raceId}/{participantId}/process-result` endpoint
- `ProcessParticipantResultAsync` in ResultsService: validates participant exists, calls `ReprocessParticipantInternalAsync`
- `ReprocessParticipantInternalAsync`: touches result `UpdatedDate` to trigger DB recalculation, then calls `CalculateResultRankingsAsync`

**BUG API-8 + API-10 (Gender filter + race contamination)**
- `PublicResultsService.GetPublicResultsAsync` and `GetPublicGroupedLeaderboardAsync`: gender input normalized (M/Male→"M", F/Female→"F") before DB filter
- Race name filter changed from `Contains` to exact `==` match to prevent cross-race results leaking
- `MapToResultDto`: gender displayed as "Male"/"Female" (from stored "M"/"F")

**BUG API-9 (IsTimed gate)**
- `ProcessRFIDImportAsync`: checks `Race.IsTimed` before doing any EPC-to-participant mapping; returns `Status = "Skipped"` when false

**BUG API-13 (Dashboard stats)**
- New `GET /api/dashboard/event/{eventId}/stats` → `EventDashboardStatsDto` (gender/category/race breakdowns)
- New `GET /api/dashboard/race/{eventId}/{raceId}/stats` → `RaceDashboardStatsDto` (gender/category, fastest/avg times)
- `EventDashboardStatsDto`, `RaceDashboardStatsDto`, `GenderBreakdownItem`, `CategoryBreakdownItem`, `RaceStatItem` created in `Runnatics.Models.Client/Responses/Dashboard/EventDashboardStatsDto.cs`

**SQL Script** (not executed): `db/scripts/TestingFeedback_Round1_SchemaChanges_20260515.sql`
- Adds: `ManualDistance` (Checkpoints), `IsMandatory` (Checkpoints), `IsTimed` (Races), `IsMultipleEpc` (RawRFIDReading), `TotalTagsInFile`/`TagsProcessed` (UploadBatch)
- Drops duplicate FileHash unique index; adds performance indexes

---

### 2026-05-15 — Testing Round 1 Bug Fixes (Session 2 — commit 8b25f20)

Branch: `bugfix/testing-round-1`.

**BUG API-5 (Split time correctness + IsMandatory status + gender rankings)**
- `CalculateSplitTimesAsync`: added `previousCheckpointId` tracking; set `ToCheckpointId`, `FromCheckpointId`, `SplitTime` (TimeSpan) on new SplitTimes records — these were all missing (required fields defaulting to 0); skip readings with null/zero GunTime
- `CalculateResultsAsync`: replaced "highest distance checkpoint = finish" logic with IsMandatory-based status:
  - All mandatory checkpoints covered → "Finished" (finish time from mandatory checkpoint with highest distance)
  - Some mandatory covered → "DNF"
  - No mandatory covered → "DNS"
  - Falls back to single highest-distance checkpoint if no IsMandatory checkpoints are flagged
- `CalculateSplitTimeRankingsAsync` and `CalculateResultRankingsAsync`: fixed gender filter from `"Male"/"Female"/"Others"` to `"M"/"F"` — gender is stored as single character via ValueConverter
- `ResultStatus` constants class created at `Runnatics.Models.Data/Constants/ResultStatus.cs`

**BUG API-14 (Performance hardening)**
- Azure SQL retry: already configured (`maxRetryCount: 5`, `maxRetryDelay: 10s`) — no change needed
- Added Brotli + Gzip response compression (`CompressionLevel.Fastest`, `EnableForHttps = true`)
- Added `AddOutputCache` with `"PublicResults"` policy (30s TTL, tag `"public-results"`)
- `[OutputCache(PolicyName = "PublicResults")]` added to 5 GET public endpoints: `GetEventById`, `GetResultByBib`, `GetResultFilters`, `GetRaceFilters`, `GetBracketFilters`
- Cache evicted with `IOutputCacheStore.EvictByTagAsync("public-results")` in `EventsController.Update` when `request.EventSettings.Published == true`

**Build**: 0 errors, pre-existing warnings only.

---

## 2026-06-20 — Durable manual-time overrides (raw / override / derived three-layer model)

**Problem:** Manual time edits only lived in `ReadNormalized`, which `ClearProcessedDataAsync` always deletes → every manual correction was wiped by clear+reprocess.

**Fix (all-in-one, API):**
- **New table `ManualTimeOverrides`** (`db/scripts/Add_ManualTimeOverride_20260620.sql`, idempotent, run manually) — durable authoritative input; no clear query touches it. Filtered unique index `(ParticipantId, CheckpointId) WHERE IsDeleted=0`.
- **Entity + config** `ManualTimeOverride` / `ManualTimeOverrideConfiguration`; registered in `RaceSyncDbContext`.
- **Write path** `ResultsService.RecordManualTimeAsync` STEP A-1: upsert the durable override (the single active row) alongside the existing `ReadNormalized` display write. `ManualCrossingUtc` = IST→UTC via `Event.TimeZone`.
- **Phase 2.4** `RFIDImportService.ApplyManualOverridesAsync` — runs in `ProcessCompleteWorkflowAsync` after Phase 2 (normalize), before Phase 2.5 (splits). Upserts `ReadNormalized` for each active override (ChipTime/GunTime/NetTime, IsManualEntry=true). Covers reprocess + clear+reprocess + race move (all funnel through this workflow). NoTracking-safe: load-once + UpdateRange/BulkInsert.
- **Revert** `ResultsService.RemoveManualTimeAsync` + `DELETE .../participant/{id}/manual-time?checkpointId=` on RFIDController — soft-deletes override + manual ReadNormalized/SplitTimes, recomputes status (mandatory per-distance gates), re-ranks. Releases the filtered unique slot so re-override works. May flip Finished→DNF if manual-only.
- **Move-invalidation** `ParticipantImportService.MoveParticipantToRaceAsync` step 3b — soft-deletes the participant's overrides (source-race CheckpointId is meaningless in the target).
- **Docs** `context/manual-overrides.md` (three-layer invariant) + CONTEXT.md router line.

**Staged UI (blocked on auth):** "Remove manual time" button must warn when the checkpoint has no underlying raw read (runner may become DNF). Endpoint doesn't block; UI warns.

**Build**: 0 errors, pre-existing warnings only. SQL to be run manually by Kunal; test matrix on race 47 (set → reprocess persists; clear+reprocess survives; revert; re-override after revert; move invalidates).

---

## 2026-07-02 — Race 65 start mis-selection: pass-collapse floor invariant + checkpoint-config validation (scope B)

**Problem (race 65, event 38, bib 5176):** The Phase 1.5 pass-collapse merged the pre-gun cluster (05:31:22–05:32:37 IST) with the real start read (05:33:34 — 57s later, under the 300s default `PassGapThresholdSeconds`) and kept a pre-floor representative. Gun-window start selection then ran on collapsed reps, found nothing in-window until the finish-area read, and normalized 05:51:53 as the "start" (GunTime 18:53). The active checkpoint config was CLEAN (shared start/finish mat: Dev 2 primary + Dev 1 child at 0.0/5.0, Dev 11 at 2.5); the earlier "malformed config" 8-row dump included deleted rows. Historical note: that deleted state (duplicate primary Finish rows + circular Dev1↔Dev2 parent/child) is exactly what the new validator rejects.

**Fix 1 — collapse floor invariant (`RFIDImportService.AssignCheckpointsForLoopRaceAsync`):**
- INVARIANT: the pass-collapse must NEVER merge reads across the valid-start floor. Start selection now runs PRE-collapse on RAW reads — earliest in `[floor, ceiling]` per start-bound shared group (same `StartWindow` helper). Reads before the chosen start are excluded from collapse entirely (never assigned); the chosen start is PINNED as its pass-0 representative (dedup-window keep-LAST suppressed when a valid start exists).
- The old post-collapse gun-anchored selection block reduced to plain per-group chronological ordinal assignment (one selection site). No-valid-start groups unchanged: chronological ordinals → pre-floor "start" reaches Phase 2/3 as the INVALID placeholder → early-start DNS rule.
- Behavior nuance: for an in-window start cluster the representative is now the EARLIEST in-window read (was: LAST within dedup window) — matches the deployed "earliest in [floor, ceiling]" rule (Phase 2 `participantStartTimes`, display).

**Fix 2 — checkpoint-config validation (fail loudly, never silently guess):**
- New `Runnatics.Services/RFID/CheckpointConfigValidator.cs` — checks (a) duplicate PRIMARY checkpoints at one distance, (b) circular parent/child device refs incl. self-parent (DFS over all edges, cycles deduped), (c) contradictory roles: device both primary and child, or child of multiple parents, (d) same device with 2+ rows at equal distance.
- Wired at: `ProcessCompleteWorkflowAsync` entry (REQUIRED there — a Phase 1.5 failure is deliberately downgraded to a warning at :1541 and the workflow would continue into Phase 2 on stale assignments); `AssignCheckpointsForLoopRaceAsync` after checkpoint load (covers direct Phase 1.5 calls); `CheckpointService` Create/BulkCreate/Clone/Update/AddLoops (reject at authoring time; Update validates a would-be copy before mutating; deletes can't introduce these violations, left unguarded).
- `LoopRaceCheckpointAssigner.OrderCheckpointsByDistance` equal-distance warning promoted to `InvalidOperationException` (service catch → Status="Failed").

**Confirmed:** `HasLoops=0` does NOT gate the shared-device path — routing is `sharedDeviceExists` (any device with ≥2 checkpoint rows); `HasLoops` only picks Cyclic vs Sequential.

**Known follow-ups (reported, NOT fixed — out of approved scope):**
- Phase 2 `startCheckpointId = allCheckpoints.OrderBy(DistanceFromStart).First()` is an unstable tie among same-distance rows (396 vs 429); works today because the primary has the lower Id / DB order, but should prefer the primary row explicitly.
- Orphan-child check (child row whose ParentDeviceId has no primary row at the same distance → Phase 2 merge silently skips) — candidate validator check (e).
- Save-time guard blocks edits to races whose config is ALREADY invalid until the named rows are fixed (deletes always allowed) — intended fail-loud behavior, noted for support.

**Tests:** new `CheckpointConfigValidatorTests` (13) incl. race-65 active-config-must-pass and 8-row historical-state-fires-a/b/c/d fixtures + assigner throw test. Full suite 31/31 green (`DOTNET_ROLL_FORWARD=LatestMajor` needed locally — no .NET 8 runtime on this box).

**Build:** 0 errors, pre-existing warnings only. NOT committed — awaiting prod verification: reprocess race 65 → bib 5176 start 05:33:34, finish ~05:51:53, net ~18:19, GunTime at start ≈ 34s. EarlyStartCutOff stays 1s (cutoff was not the bug).

---

## 2026-07-02 (2) — Result-calc edge-case regression suite (56 new tests) + 7c/7d fixes

**Goal:** turn every rule from this session (valid-start window, DNS truth table, collapse fix, finisher-safe, ranking, config validation) into permanent tests. 87/87 green.

**Test-enablement extractions (behavior-preserving, verified by the untouched 31 pre-existing tests + zero build errors):**
- `ResultClassifier.cs` (NEW) — the Phase 3 Finished/DNF/DNS truth table as a pure function; `CalculateRaceResultsAsync` now calls it (replaced the inline if/else + `HasValidStart`/`IsEarlyStart` locals).
- `LoopRaceCheckpointAssigner.CollapseIntoPasses` (NEW static) — gun-anchored start selection + pass-collapse + ordinal assignment extracted pure from Phase 1.5; service calls it and logs from the returned counts.
- `PassCollapseSettings.cs` (NEW) — DedUpSeconds/PassGapThresholdSeconds defaults+guards (30s/300s, ">0" rule), mirrors StartWindow.
- `CheckpointGates.cs` (NEW) — deterministic start/finish gate selection: distance → PRIMARY before child → Id.

**In-scope fixes (pre-approved):**
- 7c: validator check (e) — orphan child (no parent row at the child's distance, 0.001 KM tolerance mirroring the Phase 2 merge) → violation.
- 7d: Phase 2 (`DeduplicateAndNormalizeAsync`) start/finish gate ids now via `CheckpointGates` (was OrderBy(distance).First() = DB-order tie between primary 396 / child 429). Phase 3 already used primaries-only; `ResultsService.LoadCheckpointTimesAsync:956` filters primaries → left unchanged.

**New tests (56):** StartWindowTests 8 (window edges, seconds-not-minutes, defaults incl. negative, null gun, IST midnight rollback); ResultClassifierTests 19 (truth-table rows a–l incl. boundary inclusivity, early-taint-beats-finish, finisher-safe, negative-finish precedence, no-window fallback, cross-UTC-midnight); PassCollapseTests 8 (bib-5176 end-to-end collapse→assign→dedup = start 00:03:34/net 18:19, earliest-in-cluster, DNS placeholder keep-LAST, finish-never-start, staggered cross-read excluded across midnight, per-EPC independence, non-start groups untouched, settings defaults); RankCalculatorTests 13 (net/gun bases, BUG-24 per-view, tie chain → ParticipantId, order-stable fixed point, null-times-last, M/F-only gender, Unknown category, ResolveBasis matrix); CheckpointGatesTests 5; validator (e) 3.

**Verification split:**
- UNIT-TESTED: sections 1 (a–l), 2 (a–e), 3 (a–e core), 4a (classifier part), 6 (a–f + determinism core of g), 7 (a–e), 8a.
- CODE-VERIFIED (line-cited, no test): 3f HasLoops routing (`sharedDeviceExists` :4300s), 4e negative NetTime → null (:2243-2252), 2d no AddMinutes consumer anywhere (grep — the queued Part A concern is already resolved in code), 6d/6g caller contracts (Status=="Finished" loads; both paths call ApplyStoredRanksAsync :1398/:3106/:4010), 8b edge conversion.
- INTEGRATION/PROD-VERIFY ONLY: 4b/4c/4d Phase-2 StartTime guards (prod-verified in d6232e1/46ec16d), section 5 manual-override flows (EF-heavy; prod-verified per 2026-06-20 entry), Phase 2 placeholder retention + gun-clamped baseline.

**No new failures found:** every tested rule passed on first run; the only "failures" were the two pre-approved gaps (7c/7d), fixed. Build 0 errors. NOT committed/pushed per instruction. Race-65 prod reprocess verification still outstanding for commit 996b2e0.

---

## 2026-07-02 (3) — Splits/cumulative rebased to the runner's own valid start (net baseline)

**Client rule:** Start row = 00:00/00:00 always; cumulative@N = crossing N − runner's valid start crossing; INVARIANT cumulative@Finish == NetTime; the gun-to-start offset (Gun − Net) is a separate value, never a split/cumulative.

**Decisions (all five confirmed):** (1) Option B — stored `SplitTimes.SplitTimeMs` STAYS gun-based (checkpoint ranks + legacy rows depend on it); net cumulative derived at read time via ONE pure helper. (2) No-start-read finisher keeps NULL NetTime (invariant vacuous; cumulative shows gun-based). (3) DNS split rendering untouched. (4) Comparison diffs switched to net. (5) `SplitTimes.Rank` basis untouched (gun-ordered) — product decision, flagged.

**New:** `Runnatics.Services/SplitBaseline.cs` — `BaselineMs(startRowSplitTimeMs, lateStartCutOffSeconds)` (validity gate via `StartWindow.LateSeconds` defaulting — a raw column read of a null cutoff would invalidate every start row) and `CumulativeMs`. Valid start (≤ ceiling) → baseline = max(0, offset) (mirrors BUG-27 gun clamp); late placeholder / no start row → baseline 0 = gun (matches gun-clamped NetTime, so the invariant holds for late-only finishers too).

**Consumers rewired (the 5-implementation divergence closed):** `PerformanceMetricsBuilder` (start row keyed on DISTANCE not row index — a missed-start runner's first row is no longer zeroed/baselined; signature now takes LateStartCutOff); `ResultsService.GetParticipantSplitsAsync` (+pace recomputed from net cumulative — stored Pace may be stale gun-based); `PublicResultsService` participant detail, comparison (per-runner baselines incl. cross-race, diffs net-based), results list `MapToResultDto`; `ResultsExportService` Excel splits. Includes threaded: Race→RaceSettings at 4 load sites + `LoadParticipantDataAsync` returns per-runner baseline.

**Writers:** Phase 2.5 stores Start-row `SegmentTime = 0` going forward (display forces 0 regardless — old rows' stored offset is masked, NO reprocess needed); interactive `CalculateSplitTimesAsync` same + stored `Pace` now from net cumulative.

**Decision-2 flag confirmed:** RankOnNet=true + Finished with null NetTime → `RankCalculator.OrderByBasis` sorts null as long.MaxValue → ranks LAST, never above real times (pinned by `NullTimes_SortLast_NotFirst`).

**Not changed (noted):** W2's pre-existing `GunTime <= 0` row skip (a start crossing exactly AT the gun gets no split row); checkpoint-rank basis; DNS split rows still render.

**Tests:** +10 `SplitBaselineTests` (defaulting trap, validity gate, bib-5176 math incl. Finish==NetTime invariant and Gun=Net+offset reconciliation, late-only == gun-clamped NetTime, clamps). 97/97 green; build 0 errors.

**Prod verify pending:** bib 5176 → Start 0/0, 2.5K ≈ 8:26 net, Finish cumulative 18:19 == NetTime; a late-start finisher → cumulative == gun-clamped NetTime; C1–C5 all show identical numbers; Excel matches screens. NOT committed — sits alone in the working tree (prior work already pushed), ready to be its own commit.

---

## 2026-07-02 (4) — UI: page-level busy lock on Process Result / Clear Processed Result

**Ask:** while Clear Processed Result runs, Process Result and every other button on the participants page must be unclickable — and vice versa.

**Change (UI repo, `ViewParticipants.tsx`):** derived `resultsBusy = processingResults || clearingResults` and applied it page-wide. Previously each button only disabled ITSELF, so a clear and a process could race each other server-side.
- Toolbar: Add Participant / Add Range / Bulk Upload / Update by Bib / Export CSV / Columns / Process Result / Export Results (Excel) / Clear Processed Result — all `disabled` while busy; Process↔Clear get mutual-exclusion tooltips ("Wait for … to finish").
- Grid row actions (View/Edit/Delete IconButtons) disabled; the clickable bib link made inert (`if (resultsBusy) return`).
- Filter Reset button disabled. Filters/pagination left active (read-only fetches, harmless).

**Caveat (noted, not fixed):** the lock is client-side state — a page refresh mid-run re-enables the buttons while the server job continues. A durable lock needs a server-side "processing in progress" flag/endpoint; flag if it bites.

**Build:** `npm run build` ✓ (31s, 0 errors; pre-existing chunk-size warnings). UI repo working tree, not committed.

---

## 2026-07-03 — START SELECTION reverted to the historical LAST-read rule (single shared selector)

**Client-confirmed final rule:** among IN-WINDOW start reads, start = LAST read of the FIRST in-window pass (the runner LEAVING the mat) — bounded by the pass gap (`PassGapThresholdSeconds`, default 300s). The earliest-in-window selection introduced with the race-65 collapse fix (2026-07-02) was a DRIFT; the window handling from that fix (pre-floor exclusion, invalid-placeholder retention, DNS truth table) stays exactly as-is. Screenshot case (race 65, chip 44E0014498A0): cluster 05:32:50→05:33:33, next checkpoint 05:42:00 → start = 05:33:33 (was wrongly 05:32:50).

**One implementation — `StartWindow.SelectStartRead<T>`** (in StartWindow.cs, beside the window): anchors at the first in-window read; chains by pass gap; a same-pass read PAST the ceiling extends the pass but cannot win; later in-window passes (gap exceeded) are the next crossing, never the start; pre-floor reads never anchor/chain; null when no in-window read.

**Consumers flipped together (no divergence possible):**
- P1.5 shared path — `LoopRaceCheckpointAssigner.CollapseIntoPasses` chosen-start (pin/exclusion mechanics unchanged; earlier cluster reads excluded as the same crossing).
- P2 simple path — `DeduplicateAndNormalizeAsync`: `participantStartTimes` NetTime baseline (gun-clamp BUG-27 kept) AND the start-row `bestReading` (placeholder branch unchanged). P2 now reads `PassCollapseSettings.PassGapSeconds` too.
- Manual-edit + display: validity checks untouched (no selection there — they consume the stored row).
- Phase 3 / ResultClassifier: UNCHANGED (validity still "≥1 in-window read"); SplitBaseline UNCHANGED (baseline = the stored row's offset, whichever rule selected it).

**Spec:** `spec/TIMING_LOGIC_SPEC.md` — the three-way divergence note marked RESOLVED; new "NAMED INVARIANTS" section: START SELECTION INVARIANT (LAST of first in-window pass; changing requires explicit client sign-off) + the round-2 DEDUP INVARIANTS (start=LAST, others=EARLIEST).

**Tests (106/106):** 6 new `SelectStartRead` unit tests (9-read cluster → :33:33; second in-window pass ≠ start; pre-floor never anchors/chains (06:07:29/06:08:05 regression); post-ceiling same-pass read extends-but-cannot-win; null cases; bib-5176 single-read). 3 new/1 flipped collapse tests (cluster → LAST + excluded count; screenshot chip end-to-end → CP396 @ +33s; second-pass = ordinal 1; pre-floor+single-in-window). Bib-5176 regression, truth-table (19), SplitBaseline (10) all pass UNCHANGED.

**IMPACT (for prod verification):** reprocess shifts starts LATER for any runner with an in-window start cluster → NetTimes SHORTEN, standings can shift. That is the correction, not a regression. Single-in-window-read runners (e.g. bib 5176) unchanged.

**Build 0 errors. NOT committed — sits alone in the working tree for its own commit.**

---

## 2026-07-03 (2) — RULE PASS commit (a): #7 status definitions (truth-table rewrite)

**New model (client-confirmed):** per mandatory gate, is there VALID data? all → OK/Finished · some → DNF · none → DNS. Invalid reads are not data. Mandatory set = {START gate, implicitly (decision 1)} ∪ {IsMandatory} ∪ {finish fallback}. Start-gate validity = in-window crossing (`StartWindow.Contains`, inclusive). Negative finish = invalid finish data.

**KILLED (deliberate, client sign-off):** finisher-safe/Row-5 keep (no-valid-start finisher was Finished → now DNF); late-only-finisher keep (→ DNF); early-taint DNS (pre-floor + finish data was DNS → now DNF; DNS only when invalid was the only data). Reprocessing events 30/36/38 WILL flip some Finished → DNF — tell Punit BEFORE reprocessing.

**Code:** `ResultClassifier` rewritten (Classify(valid,total) + MandatoryDistances helper — distance-keyed, shared-mat safe); `StartWindow.Contains` added (THE window membership test); `ResultStatus.ToDisplay` ("Finished"→"OK", display only). Rewired: RFIDImportService Phase 3; ResultsService CalculateResultsAsync (+race/window load — was window-blind), RecordManualTimeAsync, RemoveManualTimeAsync (+window load), ComputeParticipantStatusAsync (full entity load — was anonymous-type). Display mapping applied: ParticipantResultResponse, ManualTimeResponse, participants Excel (:1833), results Excel. Public site never renders status (filter-only) — nothing to map. Grid status display lands with commit (e)'s DDL fix (same surface).

**Tests 107/107.** MEANING CHANGES (for spec honesty): RowG late-only Finished→DNF; RowI no-start-finisher Finished→DNF; RowC-late+covered Finished→DNF; RowE/RowK early+covered DNS→DNF; classifier signature now gate-coverage counts; SplitBaseline late-only test re-premised (runner now DNF; gun-fallback stays the split display rule). Unchanged rows asserted: valid-start+coverage, valid-start+gaps, invalid-only DNS, boundary inclusivity, midnight-crossing window.

**Spec:** STATUS DEFINITIONS section added with removed-rules history. NOT pushed.

---

## 2026-07-03 (3) — RULE PASS commit (b): #6 DedUpSeconds redefinition (minimum segment time)

**BREAKING (client-confirmed):** OLD rule (removed): DedUpSeconds = same-checkpoint collapse window (null/0→30s default). NEW rule: DedUpSeconds = MINIMUM SEGMENT TIME between CONSECUTIVE checkpoints; crossing at N+1 < DedUpSeconds after N's crossing → discarded; later reading ≥ threshold used; gate uninhabited (→ #7 DNF) only if nothing valid remains; null/0 = feature OFF (default REMOVED). Verbatim old→new comments at PassCollapseSettings, SequentialGateSelector, both freeze sites.

**Decoupling (per the gate-1 interaction finding):** the internal same-checkpoint collapse is FROZEN at the 30s constant — CollapseIntoPasses call site + legacy per-batch path (:1178) no longer read RaceSettings.DedUpSeconds. One-crossing-per-checkpoint is guaranteed by pass-gap chaining + Step-5 dedup + Phase-2 selection (proven in gate 1); `PassCollapseSettings.DedupSeconds` deleted, `MinSegmentSeconds` (null/0/neg → null=OFF) added.

**New `SequentialGateSelector` (pure):** per-participant gates in course order; START gate via the START SELECTION INVARIANT (SelectStartRead; out-of-window → earliest kept as INVALID placeholder, chain still anchors on it); every later gate = EARLIEST candidate strictly-after the last selected crossing (#2 offline) AND ≥ minSegment when ON; uninhabited gates don't break the chain (next validates against last selected). GREEDY NO-BACKTRACK pinned by test (starved next gate = DNF, per client rule); earliest-valid-never-hurts-next also pinned.

**Phase 2 rewired:** pre-computed chain per participant replaces the inline bestReading; unselected gates produce NO normalized row (discarded reads are not data); monotonic guard retained as no-op safety net. `readingsWithMergedCheckpoints`→gates keyed on merged (parent) checkpoint ids; start gate = CheckpointGates.Start id.

**Tests 120/120:** 12 new selector tests incl. the 5km-loop 2100s example (30min discarded → 36min finish; only-30min → DNF), sequence discard/equal-time, uninhabited-gate chaining, greedy pins, placeholder anchor, missing-start-gate, per-pair min-segment; PassCollapse settings test updated (frozen constant + pass-gap only). NOT pushed.

---

## 2026-07-03 (4) — RULE PASS commit (c): #2 sequence validation on manual time edits

**Rule:** a TYPED manual edit of checkpoint N must be STRICTLY after N−1's crossing and STRICTLY before N+1's (equal timestamps violate). Violation → HTTP 400 with a message naming the conflicting checkpoint + its event-local time ("Start time 05:45:34 must be before 2.5 KM's 05:42:01"). All checkpoints. Gap-tolerant: when the adjacent gate has no crossing, the nearest existing crossing on that side is the bound; the closest offender is the one named.

**Code:** new pure `CrossingSequence.FindViolation` (Runnatics.Services/RFID); wired into `RecordManualTimeAsync` after the 24h sanity check, BEFORE the start-window branch. TOGGLED reads (chosenRawReadId non-null) are EXEMPT from the 400 — rule #1 (commit d) accepts them and validates at processing. Controller 400 phrase-map extended with "must be before"/"must be after". The OFFLINE half of #2 (discard out-of-order reading, next candidate, DNF if none) already landed in commit (b)'s SequentialGateSelector.

**Not enforced (noted):** min-segment (#6) on typed manual edits — the client spec's #2 lists sequence only; flag if they want the min-segment 400 too.

**Tests 127/127:** 7 new CrossingSequence tests incl. the client's exact example, strict-equality violation, gap tolerance, closest-offender naming. NOT pushed.

---

## 2026-07-03 (5) — RULE PASS commit (d): #1 toggle/manual acceptance (accept → classify)

**SUPERSEDES discard-and-warn (46ec16d), decision 2:** an out-of-window START — TYPED or TOGGLED — is now ACCEPTED and stored (override + normalized row persist; BUG-27 gun clamp extended to accepted early starts); #7 classification decides the consequence (start data invalid → DNF with other valid gates, DNS when only data). `DiscardOutOfWindowStartAsync` DELETED (tombstone comment at the site). Save succeeds WITH a warning naming the consequence (ManualTimeResponse.Warning).

**Toggled/typed mid+finish gates:** the edited crossing counts as VALID data at its gate only if it passes the SEQUENCE rule and the MINIMUM-SEGMENT rule (#6, when DedUpSeconds set) against the runner's other crossings — computed at save/processing in RecordManualTimeAsync (typed sequence violations still 400 first, commit c). Invalid ⇒ stored but gate UNCOVERED → #7 DNF path + warning.

**Persistence:** ChosenRawReadId infra unchanged — toggles survive clear+reprocess via Phase 2.4; on reprocess Phase 3 re-applies the same #7 window check to the override row → consistent classification by construction.

**Spec:** discard-and-warn added to the DELIBERATELY REMOVED list with the incoherence reason. **Tests 127/127** (pure pieces — Contains, CrossingSequence, MinSegmentSeconds — already pinned; the accept-path itself is EF-heavy → prod/integration verification: toggle an out-of-window start → runner DNF + warning, revert → recompute). NOT pushed.

---

## 2026-07-03 (6) — RULE PASS commit (e): #4 status DDL + #5 DSQ (rerank, preservation, sort)

**#4 root cause:** grid/DDL got RAW stored "Finished" (`PopulateCheckpointTimesAsync :649`) which matched no DDL option → control fell back to stale Participant.Status ("Registered"). Fix: grid Status = `MapResultStatus(result.Status)` (display form OK/DNF/DNS/DSQ). RunStatus is now COMPUTED-ONLY at the request boundary: `UpdateParticipantRequest.Validate` rejects OK/DNF/DNS ("only DSQ can be set manually" → 400); reason MANDATORY for DSQ.

**#5 DSQ:** boundary normalization to the ONE stored value "DQ" (`ResultStatus.IsDsq` accepts DSQ/DQ/Disqualified any-case — enum-vs-string trap pinned by test); display label "DSQ" (`ToDisplay`). On DSQ save (`UpdateParticipantExtendedAsync`): Status="DQ" + reason, ranks NULLED, missing Results row CREATED (grid/public visibility), then `RankCalculator.ApplyStoredRanksAsync` re-ranks in memory (loads Finished only → everyone below steps up, gender+category included; NOT an RFID reprocess).

**DSQ survives every recompute path:** Phase 3 (skip-classify + keep row), `ResultsService.CalculateResultsAsync` (force-recalc never deletes DQ rows; no new row created), `ReprocessParticipantInternalAsync`, `RecordManualTimeAsync`, `RemoveManualTimeAsync` (all guard `Status != DQ` before overwriting).

**Sort (ranked OK → DNF → DNS → DSQ LAST) on every surface:** participants grid StatusOrder (:467), participants Excel export, admin leaderboard (all three rankBy branches), public results list (also fixes the EF `OrderBy(OverallRank)` SQL-NULLs-FIRST bug that would have floated DSQ/DNF to the TOP of the public leaderboard), results Excel export. `PublicResultDto.Status` added (display form) so the public site can render the DSQ label.

**Un-DSQ path NOT specified by client — not implemented** (RunStatus accepts only DSQ; reverting a DSQ currently requires a direct data fix). Flagged for follow-up.

**Tests 134/134:** +7 (ToDisplay mapping, IsDsq spellings, canonical "DQ" pin, computed-only rejection, DSQ+reason matrix). NOT pushed.

---

## 2026-07-03 (7) — RULE PASS commit (f): #3 Save & Process response completeness — RULE PASS COMPLETE

**Gap:** `ManualTimeResponse` ranks/TotalFinishers were reloaded only when computedStatus==Finished, and stored Gun/Net times were never returned — the UI needed a second fetch for the header card and couldn't reflect demotions.

**Fix:** reload the COMPLETE post-recalc result on EVERY edit (after transaction + re-rank): new DTO fields GunTimeMs/GunTime/NetTimeMs/NetTime (stored, formatted); Overall/Gender/Category ranks now from the reloaded row (null for DNF/DNS/DSQ — a demotion correctly clears the header); TotalFinishers always. Finished-only guard now scopes ONLY the completion notification. FinishTimeMs/FinishTime keep edited-value semantics (finish edits only).

**UI contract documented in the spec** (UI work gated): per-edit → re-render header card + edited grid row from the response, race-wide rank shifts still warrant a background grid refresh; bulk Process Result → counts only, UI re-fetches (existing behavior).

**Tests 134/134, build 0 errors. RULE PASS (a)–(f) COMPLETE — six commits, NOTHING pushed:** 6ee034d (#7), ef417c7 (#6), aa7ba0a (#2), 01727b9 (#1), a85ef01 (#4+#5), + this. Prod verification before push: reprocess flips Finished→DNF on old events (tell Punit FIRST); 5km-loop min-segment example; sequence 400s; out-of-window toggle → DNF+warning; DSQ → rerank/sort/label on all surfaces incl. public; DDL shows computed status; header card re-renders from edit response.

---

## 2026-07-03 (8) — UI for the rule pass (+1 API mapper guard)

**API-side prerequisite discovered while wiring the UI:** BOTH UI editors call `participants/{id}/edit-participant` (plain `ParticipantRequest` → AutoMapper onto the entity) — NOT the extended endpoint commit (e) hardened. The mapper convention-mapped `Status` onto `Participant.Status` (bypassing DSQ validation/normalization/rerank; a client omitting the field would have NULLED it). Fix: `.ForMember(Status, Ignore())` on ParticipantRequest→Participant — status is unwritable via the plain edit (computed-only, #4); DSQ goes through `PUT api/races/{raceId}/participants/{participantId}`. API 134/134 green.

**UI (Runnatics.Ui), build green:**
- `ServiceUrls.updateParticipantStatus` + `ParticipantService.disqualifyParticipant(raceId, participantId, reason)` — the one status-writing call.
- **#4/#5 Run Status DDL** (ParticipantDetail edit dialog + EditParticipant grid modal): shows the COMPUTED status ("<status> (computed)"); "DSQ (Disqualify)" is the only selectable change; reason field required (client-side guard + server 400 backstop); plain edit payloads NO LONGER send status; DSQ applied via the dedicated endpoint after the scalar save (its failure surfaces with a retry-able message).
- **#7/#5 labels**: StatusBadge + grid statusConfig + VALID_STATUSES + Participant.status union extended with OK/DSQ (DSQ styled distinct: white-on-red).
- **#1 toggle batching** (ParticipantDetail reads panel): switches now mutate LOCAL `pendingCrossings` only (pending = warning-colored); "Save & Process Result (n)" + "Discard changes" buttons; >1 read ON at one checkpoint = named conflict, save disabled; on save, per changed gate → chosen-read override or revert-to-auto, ONE refresh at the end; response `warning`s surfaced in the snackbar (severity warning). Auto-pick can still not be turned off directly (info toast).
- **#1/#3 warnings**: typed manual-time saves surface the response Warning (accepted-but-invalid → runner classified) instead of a blank success.
- **#3 refresh contract**: the detail page keeps the re-fetch model (refreshParticipant + fetchDetections after every save) — permitted by the spec contract; the response-driven render remains available for a later optimization.

**NOT wired (flagged):** public ResultsPage does not currently render a status column — `PublicResultDto.Status` is available whenever that page adds one; un-DSQ path still unspecified. Both repos in working tree, NOT committed.

---

## 2026-07-03 (9) — REVERT restores automated timing (locked anchors + pipeline funnel)

**Was:** revert soft-deleted override + gate's RN/split rows and recomputed status from REMAINING rows — never re-selected from raw → empty gate → #7 DNF (screenshot: manual start 05:29:23 reverted to a blank row).

**Now (`RemoveManualTimeAsync` rewrite, Task<bool>→Task<ManualTimeResponse?>):** delete override + gate rows + NEXT gate's split (Gap B — one gate provably sufficient: SegmentTime[i] references only crossings i,i−1; cumulative is gun-based) → funnel through `ProcessCompleteWorkflowAsync` (the same call ProcessParticipantResultAsync trusts; no hand-rolled second selection) → snapshot response (commit-f contract) + WARNING when the gate stayed empty (post-workflow row check = direct truth, covers no-reads AND all-discarded). One path serves typed overrides + toggles. Workflow-failure shape = race-move's (override gone, gate empty, Process Result recovers). IsManualTiming clearing kept; old in-method status/rank recompute deleted (pipeline owns both; DSQ preserved by Phase 3).

**Gap A — LOCKED ANCHORS (SequentialGateSelector + Phase 2):** `GateInput.LockedCrossingTime`; Phase 2 injects the participant's EXISTING normalized crossings as locked gates (query per race). Locked gates emit no selection, anchor the chain, and UPPER-BOUND earlier non-locked gates (strictly-before next locked + min-segment on BOTH sides — fresh-reprocess equivalence). Bonus fixes: late-batch one-gate-no-anchor hole AND incremental duplicate-row creation at already-normalized gates (locked wins; new candidates stay unselected until revert/full reprocess).

**Controller:** DELETE manual-time returns `ResponseBase<ManualTimeResponse>`; UI ignores body today (re-fetches) — revert-warning rendering added to the gated UI queue (spec updated).

**Tests 139/139:** +5 locked-anchor tests (no-emit+anchor, later-locked upper bound, min-segment both sides, start-past-locked uninhabited, reverted-start re-selected by the START INVARIANT under a locked finish). **Prod-verify:** race 65 manual start 05:29:23 → revert → system-selected start returns, byte-identical to fresh reprocess; revert-without-raw → warning + #7 status; toggle revert same path. NOT pushed.

---

## 2026-07-03 (10) — ASSIGN-THEN-CHOOSE, API half (unassigned reads become choosable)

**Client scenario:** the 05:32:22-class reads are unassigned BY DESIGN (pass-collapse rejects pre-start noise → no assignment) — choosing one is an operator override that must CREATE assignment state. The old chosen-read validation (`ResultsService:1659-1666`) hard-rejected them.

**API (this commit):**
- `RecordManualTimeAsync` chosen-read path: an UNASSIGNED read is now choosable — candidates = race checkpoints whose device matches the read's serial (batch serial → read serial, via the ONE `DeviceSerialResolver` map extracted verbatim from Phase 1.5's FIX #2/#9 block; Phase 1.5 now consumes the same helper — no fork possible). Target checkpoint must be a candidate (the UI supplies it — decision a: inline picker on shared mats; server NEVER guesses); 400 naming valid candidates otherwise; assignment created with audit, then the normal chosen-read flow (window/sequence/min-segment accept-and-classify, override, snapshot). Reads assigned to a DIFFERENT gate: unchanged rejection.
- **Decision b persistence:** Phase 1.5's FIX #3/#7 delete now PRESERVES assignments referenced by ACTIVE ChosenRawReadId overrides (`!IsDeleted` filter = the revert-cleanup falls out: revert soft-deletes the override → next reprocess deletes the assignment → read returns to honest "Unassigned"). Insert-collision guard: the persist step skips preserved (ReadingId, CheckpointId) pairs (unique index).

**Tests 144/144:** +5 DeviceSerialResolver pins (variants, case-insensitivity, most-specific-wins suffix collision). EF-heavy → prod-verify: choose-unassigned → reprocess → assignment survives; revert → reprocess → cleaned up; choose at an occupied gate flows through the normal conflict path.

**UI half NOT here (gated on Kunal's discriminator run):** enable unassigned-row switches, shared-mat inline gate picker, pass target checkpointId in handleSaveCrossings. NOT pushed.

---

## 2026-07-03 (11) - ASSIGN-THEN-CHOOSE complete: DTO addendum (API c0ee4aa) + UI half (UI 30b4beb)

**GO order executed. Nothing pushed - the ordered end-to-end on the test event gates the push.**

**API addendum (c0ee4aa):** the UI half needs to know, per UNASSIGNED read, which gates it may be chosen for (an unassigned row has no checkpointId). `LoadRawRfidReadingsAsync` now emits `ChoosableCheckpoints` (new `ChoosableCheckpointDto`: encrypted id + "Name (dist km)" display form) - resolved EXACTLY like RecordManualTimeAsync validates the save (batch serial -> read serial via the ONE DeviceSerialResolver map, then the device's active checkpoints in the race), loaded lazily (zero extra queries when every read is assigned). Contract: null = assigned; empty = device unmapped (NOT choosable, toggle locked); 1 = UI auto-targets; N = shared mat -> inline picker. Same resolver + same filter as the save validation = the UI can never offer a gate the server would 400. 144/144.

**UI half (30b4beb, ParticipantDetail.tsx + RfidRawReadingDto.ts):**
- Unassigned rows' switches ENABLED when choosable: 1 candidate -> toggle ON auto-targets (amber-pending); shared mat -> "Crossing at which gate?" Menu sets the target then amber-pending; 0 candidates -> stays locked ("device is not mapped in this race"). Checkpoint cell shows "Unassigned -> <gate>" while pending; OFF drops target with flag.
- New state: `pendingTargets: Record<readId, {id,name}>` (always paired with pendingCrossings[id]=true) + `gatePicker`. `effectiveGateOf(read) = checkpointId ?? pendingTargets[id]?.id`.
- `crossingConflicts` + `handleSaveCrossings` group by EFFECTIVE gate -> the save passes the resolved checkpointId to addManualTime (param existed) and an unassigned read chosen at an occupied gate surfaces the SAME named conflict (no auto-replace preserved). Discard/catch keep both maps consistent.

**FLAGGED DEVIATION (deadlock fix, needs Kunal sign-off before push):** the old guard blocked toggling the automatic (dedup) pick OFF entirely -> a gate occupied by an automatic crossing could NEVER be conflict-resolved, blocking the ordered end-to-end (choose pre-gun 05:26:22 at a Start occupied by the auto pick 05:33:33-class). Guard was implementation detail, not client rule. Now: dedup pick can go pending-OFF (amber); a save leaving a gate all-OFF with NO override = no-op + info note "the automatic crossing stays" (the guard's purpose, enforced at save, without the deadlock). ON still never silently turns anything off.

**End-to-end gate (before ANY push):** toggle 05:26:22 (unassigned, shared mat) -> picker -> Start -> resolve conflict by toggling the auto start OFF -> Save -> runner reclassifies with pre-gun chosen start (DNF + warning expected, accept-and-classify) -> revert -> auto-selection returns. Pending pushes: API dbee14d + c0ee4aa; UI 30b4beb.

---

## 2026-07-03 (12) - RFID readings panel cleanup (API d153937 + UI a0f5f48)

**API (d153937):** `RfidRawReadingDto.DeviceName` - every read's serial resolved to the friendly device name via the active-device lookup (IsActive/!IsDeleted, OrderBy(Id) for deterministic duplicate-MAC picks); batch serial -> read serial priority now lives ONCE in `DeviceSerialResolver.ResolveDeviceId` (assign-then-choose candidate resolution refactored onto it, identical semantics). Null = unmapped -> UI falls back to the serial; DeviceId stays in the payload. Devices query no longer gated on unassigned rows. +3 resolver tests, 147/147.

**UI (a0f5f48):** Device column renders deviceName (serial fallback + serial tooltip); REMOVED Gun Time/Net Time/Chip ID columns (raw + legacy fallback views; Split Times stays the authoritative timing view; participant-scoped table made Chip ID redundant); EPC footer STAYS (sole chip display). "Detections by Checkpoint" section deleted end-to-end: JSX, state, fetchDetections + effect + 4 refresh call sites, service method, ServiceUrls entry, ParticipantDetectionsResponse model (this page was the only consumer; API endpoint untouched). NON-display consumer `checkpointHasRawRead` (revert dialog's empty-gate pre-warning) re-derived from `participant.rawRfidTagReadings` (non-manual read assigned to the gate - same raw-only semantics), one fewer request per load/save. Snackbar severity union +"warning" (was already set by rule-pass code; vite never type-checks) -> `npx tsc --noEmit` clean repo-wide. Deploy order: API before UI bundle (else Device column shows serials). NOT pushed.

---

## 2026-07-03 (13) - UN-DSQ path (46f7ce1) - the flagged follow-up to a85ef01

**Boundary:** RunStatus="Recompute" (ResultStatus.Recompute + IsClearDsq, case-insensitive, disjoint from IsDsq, NEVER stored) = clear the disqualification. No reason required (the clear NULLS it; stray reason tolerated). All other manual status writes stay 400.

**Service (UpdateParticipantExtendedAsync):** guard - only when current Results.Status == DQ, else 400 "not disqualified"; clear + race-move combo also 400. On clear: reason nulled, status RECLASSIFIED from gate coverage on Results AND Participant rows via the NEW shared static `ParticipantStatusCalculator.ComputeAsync` (extracted verbatim from ResultsService's private ComputeParticipantStatusAsync - RankCalculator pattern, one classifier for single-runner reprocess + un-DSQ), then RankCalculator.ApplyStoredRanksAsync (in-memory race-wide - restored finisher re-enters, everyone below steps back down; mirror of DSQ apply; deliberately NOT a reprocess). Survives reprocess by construction (nothing DQ left -> Phase 3 dsq-skip doesn't fire).

**Response:** commit-f snapshot now populated on BOTH status-changing paths (DSQ apply + clear) on ParticipantSearchReponse - stored Gun/Net, post-re-rank ranks, DISPLAY status, new TotalFinishers field - loaded AFTER re-rank. Controller maps the un-DSQ guard messages to 400 (previously generic 500 branch).

**Tests +3 (150/150):** IsClearDsq spellings/disjointness; Recompute validates without reason; un-DSQ rank-shift pin (restored finisher slots by time, below steps down). Prod-verify: DSQ -> clear -> computed OK + ranks -> reprocess keeps it; clear on non-DSQ -> 400.

**UI QUEUED (spec gated-UI queue):** Run Status DDL "Remove disqualification" action when current = DSQ -> sends RunStatus="Recompute", re-renders from snapshot. NOT pushed.

---

## 2026-07-05 (14) - UN-DSQ UI (UI cef6f51) - "Remove disqualification" in both editors

Run Status DDL (ParticipantDetail edit dialog + EditParticipant grid modal): "Remove disqualification" option ONLY when current status = DSQ. Selecting it opens a CONFIRM dialog (symmetric friction with DSQ's mandatory reason); the DDL value never changes until the server confirms. Confirm -> `ParticipantService.removeDisqualification` (PUT RunStatus="Recompute", no reason). Success -> render what the classifier said (never assume OK): new `ParticipantStatusSnapshot` model; detail page applies snapshot (status chip, header gun/chip times, rankings) then re-fetches (race-wide shifts); grid modal toasts the recomputed status, onUpdate re-fetches the list, closes. Errors: `extractErrorMessage` now reads the endpoint's `{ error: "..." }` shape; 400 (stale UI) -> server message + re-sync. tsc clean, build green. Spec queue item marked SHIPPED. Requires API 46f7ce1 (pushed). Prod-verify: DSQ -> ranks shift -> Remove -> computed status + ranks restore, others shift back; 400 path on a non-DSQ runner. NOT pushed (UI cef6f51 + this docs commit).

---

## 2026-07-05 (15) - FINISH CEILING at Races.EndTime (c51ce2e)

**Client rule:** finish-gate reads after Races.EndTime are INVALID; valid finish = FIRST read <= EndTime (INCLUSIVE); only-after -> gate empty -> #7 DNF. No new status logic. OPEN client question: all gates or finish-only -> built finish-only (gate = max DistanceFromStart), every consumer gate-parameterized (one-line flip).

**One source of truth:** `StartWindow.FinishCeiling(start, end)` (null = OFF: EndTime null / SANITY EndTime<=StartTime -> unset + caller-logged warning; the LateStartCutOff=60 lesson) + `StartWindow.WithinCeiling` (inclusive). UTC throughout.

**Consumers:** Phase 2 candidate filter (selector untouched; all-filtered gate stays uninhabited); Phase 3 finisher/ranking exclusion + finish-gate invalidation + AGGREGATE FLAG ("N finisher(s) read after Race.EndTime - flagged DNF; nearest miss hh:mm:ss past") in message + new `CalculateResultsResponse.FinishCeilingNote` + workflow Warnings; RecordManualTimeAsync accept-and-classify (post-EndTime typed/toggled finish -> accepted + display-reason warning, gate uncovered; stored post-ceiling rows seen through in the covered set); ParticipantStatusCalculator + CalculateResultsAsync covered-set filters. All four classification sites identical.

**Tests +4 (154/154):** inclusive boundary, null-off, sanity-unset (equal+earlier), IST-midnight UTC math. **Kunal pre-verify data check:** SELECT Id,Title,StartTime,EndTime,DATEDIFF(MINUTE,StartTime,EndTime) FROM Races WHERE IsActive=1 AND IsDeleted=0 - flag null/equal/absurd windows BEFORE the rule can bite. Prod-verify: post-EndTime finisher -> DNF + workflow warning w/ nearest miss; at-EndTime -> OK; typed post-EndTime -> warning + DNF. NOT pushed (also pending push: un-DSQ UI cef6f51 + docs b1bbe66).

---

## 2026-07-05 (16) - GATE-MERGE (Punit point 2, bib 1002): one logical gate = primary + children, keyed by the PRIMARY id

**TRACE (item 1):** the child->parent fold lives in Phase 2 (since 20983e7, 2026-01-29) - P1.5 is per-device BY DESIGN (shared-group pass-collapse never mixes devices; the merge is P2's job). The automatic path DOES fold when the map resolves. What could never heal: (a) existing child-owned RN rows are IMMORTAL under incremental reprocess (existingNormalizedReadIds + locked anchors keyed UNFOLDED - pattern #2 skip-guards); (b) Phase 2.4 re-applied a child-keyed override under the child id EVERY rebuild; (c) the readings DTO surfaced the CHILD id, so every toggle targeted 402 and landed "where nobody looks"; (d) if the parent linkage was set AFTER first processing (or a pre-Jan build processed it), P2 normalized per-device once - then (a) froze it forever. Race 65 healed because clear+reprocess ran with a resolving map; race 66's state predates its now-correct linkage and nothing could repair it. Origin discriminator SQL handed to Kunal (RN 83935/83936 CreatedDate vs Checkpoints 401/402 audit dates + override existence).

**FIX - ONE canonical fold `CheckpointGates.CanonicalGateMap` (child id -> primary id; EXACTLY the P2 merge rule + validator check (e)), applied at EVERY id-keyed surface:**
- P2: locked-anchor keys, override-pair skip set, and (already) candidate grouping all fold; start row via CheckpointGates.Start (never index math).
- P2 SELF-HEAL (new, top of every DeduplicateAndNormalize run): active RN rows under a CHILD id are soft-deleted (with their child-keyed/chained splits) BEFORE existingNormalizedReadIds is computed -> their raw reads re-enter the candidate set at the PRIMARY gate; response message reports the healed count. One reprocess repairs a double-gate race - no SQL repair needed.
- P2.4: override target folds to the primary; kept row re-owned (CheckpointId rewritten); new rows ALWAYS under the primary; (participant, LOGICAL gate) grouping collapses legacy child-keyed duplicates.
- RecordManualTimeAsync: target folds at entry; gate identity/start/finish by DISTANCE (index math over raw rows is undefined with same-distance siblings); override upsert + STEP A0 sole-crossing collapse match the gate's SIBLING ids and re-own by primary; assignment validation folds; FromCheckpointId = previous GATE's primary.
- RevertManualTime: folds + cleans ALL sibling-id overrides/RN/splits at the gate (a leftover child-keyed override would resurrect via P2.4).
- Readings DTO: read's gate = assignment folded to primary (id the UI sends back), HasActiveOverride + choosable candidates folded/deduped.
- INVARIANT PINNED (+5 tests, 159/159): CanonicalGateMap race-66 shape + race-65 mirror + orphan-child no-entry + Canonical identity + `CrossDeviceReadsAtOneGate_MergeToOneSelection_UnderPrimary_LastWins` (the bib-1002 repro: child :49 + primary :52 -> ONE gate under 401, LAST wins :52).

**Classifier (item 5) CONFIRMED distance-keyed:** ResultClassifier.MandatoryDistances returns DISTANCES ("two devices at one distance are ONE logical gate") - child rows flagged IsMandatory=1 (402/405 are) can never count as separate required gates. No change.

**Export (item 7) CONFIRMED data-driven:** ResultsExportService columns come from SplitTimes rows (GroupBy ToCheckpointId), not checkpoint config - phantom "(0.000)"/"(10.000)" columns = child-keyed splits; the self-heal deletes them and P2.5 rebuilds under primaries, so the columns fall out with NO export change.

**SWEEP (item 6):** read-only SQL handed to Kunal - counts participants with active RN rows under BOTH a primary and its child at one distance, race-wide; repair = reprocess each affected race (self-heal). 

**UI (commit 2, Runnatics.Ui):** panel button renamed "Save & Process Result (n)" -> "Save Crossings & Reprocess (n)" (header participant-save keeps its label - it was the identical-label trap that silently discarded toggles); guard dialog "You have unsaved crossing selections - discard?" on the header save AND Back-to-Participants while amber toggles pend (confirm = discard + continue; cancel = stay); beforeunload guard for hard nav. Known limit: sidebar SPA navigation is not blocked (no data-router; noted, not built).

**Prod-verify (gates the push):** test event race 66: reprocess -> heal note in response, ONE start row (401, :52), export has NO phantom child columns; panel: :52 OFF + :49 ON -> Save Crossings & Reprocess -> override persists AGAINST 401 with ChosenRawReadId=:49-read, times recalc from :49, survives reprocess; sweep query count reported; affected races reprocessed. PUSHED 2026-07-05 on Kunal's order (API 6cb90af + UI 8d02d0c) with acceptance run PENDING - sweep found 3 affected participants (races 47/49/66); race-66 discriminator still open (override-vs-old-build), acceptance start value is :49 if a 402-keyed override exists, :52 if not; race 47 bib 2262 net may shift ~10.5s (gun-position dependent) - reprocess of 47 gated on Kunal.

---

## 2026-07-07 (17) - FROZEN NET on manual edit/toggle (bib 1002): Phase 2.45 derived-time reconciliation + save path funnels

**Punit's definitive evidence:** two toggles at gate 401, start chipTimeMs 112000 vs 49679, but gunTimeMs (1943591) and netTimeMs (1891104) BYTE-IDENTICAL - and gun-net = 52487 = exactly the :52.487 start, proving net was frozen at a prior run's baseline.

**Root cause (two halves):** (1) RecordManualTimeAsync updated the edited gate's RN row + STEP A touched Results times ONLY when isFinish, then ran a RANKING-ONLY recalc - a start edit never recomputed Results.Gun/NetTime or downstream rows. (2) DEEPER: even the full funnel would not have fixed it - Phase 2 computes Gun/Net only for rows it CREATES (locked/existing rows keep stale values), Phase 2.4 only for the overridden row (with DIVERGENT earliest-row net math), and Phase 3 COPIES the finish RN row's frozen NetTime into Results verbatim (RFIDImportService:3200). The REVERT path (which already funnels) had the same latent frozen-net bug. The manual-overrides.md "known edge" documented this and wrongly claimed full reprocess was unaffected - corrected.

**FIX:**
- **`DerivedTimes` (new, pure):** THE stored Gun/Net math - NetBaseline (gun-clamped in-window SelectStartRead winner; gun fallback when start data out-of-window; null when no start row) + ForRow (start rows Net=Gun; negative net -> null). Mirrors Phase 2 exactly = idempotent over fresh P2 output.
- **Phase 2.45 (`RecomputeDerivedTimesAsync`, between 2.4 and 2.5, in ProcessCompleteWorkflowAsync):** re-derives Gun/Net for EVERY active RN row of the race from CURRENT crossings; resets (soft-deletes) any participant's split chain whose SplitTimeMs/SegmentTime no longer matches the recomputed chain (or is orphaned) so Phase 2.5 rebuilds with its own single-source math. Idempotent; also HEALS legacy stale rows on every reprocess (pattern #2 killer). Response carries DerivedTimesRecomputed/SplitRowsReset.
- **RecordManualTimeAsync:** ranking-only CalculateResultRankingsAsync REPLACED by the full funnel (ProcessCompleteWorkflowAsync) after the save transaction - same path revert trusts; STEP A-1/A0/A/C/D keep the immediate writes (failure resilience: override committed even if workflow fails, with explicit "run Process Result" error). Response + completion notification now read the STORED post-pipeline Results (times, ranks, status - DSQ-preserved truth).
- **Tests +8 (167/167):** NetBaseline four start states, ForRow rules, and the PINNED repro `StartToggle_5249_Vs_49679_RecomputesNet_NeverByteIdentical` (exact prod numbers: net 1891104 vs 1893912, gun-net = start offset).

**FLAGGED (not fixed, pattern #1):** ResultsService.CalculateResultsAsync (standalone Calculate Results endpoint) still sets GunTime=NetTime=FinishTime from gun-based splits - divergent dual implementation, needs its own pass or retirement onto the funnel.

**Prod-verify (gates push):** toggle start :52 vs :49 -> netTimeMs MUST differ by 2808ms between responses; header + Split Times reflect it; gun-net = chosen start's offset; cumulative-at-finish = net; survives refresh AND reprocess; revert start -> net recomputes to automatic baseline. PUSHED 2026-07-07 on Kunal's order (d8000e6) with the verify run PENDING.

---

## 2026-07-07 (18) - REGRESSION FIX: EF double-tracking 500 on toggle (SplitTimes) - fresh-scope funnel + tracker-bypass Phase 2.45

**Symptom:** PUT manual-time 500 "instance of entity type 'SplitTimes' cannot be tracked because another instance with the same key is already being tracked" - introduced by d8000e6 (save path now funnels). Crossing/override SAVED (transaction commits first); the recompute died.

**Mechanism (invariants pattern, 4th hit):** global NoTracking = no identity resolution; SaveChanges does NOT detach. The save transaction leaves STEP A-1/A0/A/B/C/D instances TRACKED in the request context; the funnel then re-loads the same rows as fresh instances. Phase 2.4 collided FIRST on the RN row but its catch SWALLOWED it into a warning (silently no-opping the override apply); Phase 2.45's split-chain reset then swept the edited gate's split row (guaranteed mismatch: STEP C writes start SegmentTime=chipTimeMs, pipeline rule is 0) and its UpdateRange attach threw fatally. BONUS within-run hazard found: even on a clean context, 2.4 leaves its toUpdate RN instances tracked and 2.45 re-loads + corrects those same rows (2.4's net math is divergent by design) -> same throw on the PLAIN reprocess path whenever a mid-gate override's net differs.

**Fix (both layers, per the documented patterns):**
- **Phase 2.45 -> BulkUpdateAsync** (EFCore.BulkExtensions, tracker-bypass - Phase 3/RankCalculator precedent) for RN changes + split resets; no SaveChanges; deliberately NOT inside ExecuteInTransactionAsync (the Phase-2.4-documented bulk-in-strategy-transaction hazard); idempotent + self-reconciling so partial failure heals next run. Kills the within-run 2.4->2.45 collision on every entry point.
- **Fresh DI scope for the funnel** in RecordManualTimeAsync AND RemoveManualTimeAsync (scopeFactory -> IRFIDImportService): the funnel always starts with the clean context the reprocess endpoint gets - one owner of the entities per workflow run. Also un-breaks Phase 2.4 on the save path (its swallowed collision meant re-own/collapse never ran). UserContextService reads via IHttpContextAccessor (AsyncLocal) -> scoped run keeps the caller's identity. ProcessParticipantResultAsync left as-is (documented: nothing tracked before its funnel).
- ef-tracking-invariants.md: +funnel-from-a-request rule, +pipeline-phase rewrite rule, +SaveChanges-does-not-detach note.

**Build 0 errors, 167/167. PUSHED 2026-07-07 on Kunal's order (c179270)** - master no longer carries the broken d8000e6 state; toggle verify still to run against the deployed build. Verify: toggle bib 1002 :52<->:49 -> no 500, net 1891104<->1893912, splits rebuild, response consistent; full reprocess clean; revert clean.
