# QUEUED — Combined audit: (A) cutoff unit-mismatch fix + (B) datetime-UTC sweep

**Status:** QUEUED. Do NOT start until BOTH prerequisites close:
1. **Race 49 `Races.StartTime` corrected in prod** (must be `2026-05-10 00:59:00` UTC = 06:29 IST, the 5K gun — NOT `23:59` which is the 21K gun). This is the real upstream blocker.
2. **Commit 1 (Phase 1.5 gun-window start selection) prod-verified** — race 49 reprocessed, 2133 → 06:29 start, races 47/48 diffed and unchanged.

**Separate commit from commit 1. Report (one research doc) BEFORE any code.** Part A is a confirmed code fix; Part B is verify-and-document, but any invariant violation it finds becomes its own flagged fix. Keep the two outcomes separately labeled.

Why combined: both are "stored meaning must match how code consumes it." The cutoffs are applied to gun times (UTC datetimes), so the datetime invariant (B) underpins the cutoff arithmetic (A). They meet at `gun ± cutoff` = (UTC instant) ± (correctly-united duration); auditing together confirms both halves in one pass.

---

## PART A — EarlyStartCutOff AND LateStartCutOff unit-mismatch audit + fix

**Confirmed prod data (RaceSettings, race 47):** `EarlyStartCutOff = 300`, `LateStartCutOff = 1200`, `DedUpSeconds = 0`, `PassGapThresholdSeconds = NULL`.

**Confirmed bug (Early):** column is seconds (`.claude/DB Tables.md:485` "Early start cutoff (seconds)", UI default 300) but `RFIDImportService.cs:4412` consumes it via `AddMinutes(-earlyStartCutOff)` → 300 read as 300 **minutes = 5 hours** pre-gun. Siblings `DedUpSeconds` + `PassGapThresholdSeconds` are consumed correctly as seconds (`.TotalSeconds`, `:4218-4219`). Early is the odd one out; its in-code default (`10`) was written assuming minutes — self-contradictory.

**Tasks:**
0. **Scope which RaceSettings rows exist.** Confirmed query only returned race 47. Do races 48 & 49 have RaceSettings rows, or do they fall back to in-code defaults? Report all three races' RaceSettings (values or absence). The default's unit matters as much as the stored value.
1. **EarlyStartCutOff — every read site.** Confirm `:4412 AddMinutes` is the sole consumer; check for any other. Fix: `AddMinutes(-earlyStartCutOff)` → `AddSeconds(-earlyStartCutOff)`.
2. **LateStartCutOff — every read site.** Minutes or seconds? (See OPEN QUESTION below — may be unused.) If consumed via `AddMinutes` or any `/60`,`*60` mismatch, same bug → fix to seconds. Confirm the schema's documented unit; make consumption match.
3. **Defaults — both default IN SECONDS when DB value null/zero:**
   - Early: in-code default `10` (assumed minutes) → **300 seconds** (matches UI default).
   - Late: confirm in-code default + assumed unit → set sensible SECONDS value (likely 1200s = 20 min, matching UI). State intended value.
   - Confirm what each null/zero path does today vs after fix.
4. **Write path — verify BOTH stored as seconds on save** (UI sends seconds → DB stores seconds, no conversion). Confirm, don't assume.
5. **Whole-group sweep — audit EVERY other `*CutOff` / `*Seconds` / `*Threshold` setting in RaceSettings** for the same minutes-vs-seconds mismatch. List each: schema unit vs consumption unit; flag mismatches. Fix the CLASS, not two instances.

**Verify (Part A):**
- Early 300 → 300s (5 min pre-gun), not 5 hours.
- Late 1200 → 1200s (20 min post-gun), not 20 hours (or: confirmed unused → different remedy).
- Null/zero DB value → Early 300s, Late its sensible seconds default.
- Round-trip via UI (write → read → consume) consistently in seconds.
- Races 47/48/49 still process; no legit early/late starters wrongly dropped/admitted.
- Commit 1's gun-window selection `[gun−5, gun+15]` remains independent of these load-window widths.

### >> SEEDED PRE-FINDINGS (from the 2026-06-28 session — start from these facts) <<
- **The `> 0` guard (`:4218`, `:4220`) means BOTH 0 AND null fall to the in-code default.** So race 47's `DedUpSeconds = 0` and `PassGapThresholdSeconds = NULL` both use defaults (30s, 300s); same pattern on `EarlyStartCutOff`. Part A task-3 must treat this as a **null-OR-ZERO** path, not just null. AND decide intent: should a stored `0` mean "use default" (current behavior) or "zero window"? The `> 0` guard conflates them — flag whether that's correct.
- **LateStartCutOff: defined/mapped (entity / request / response / config / AutoMapper) but NO consumption site found in `RFIDImportService` as of this session's grep.** OPEN QUESTION: is it **UNUSED** (stored, never read → `1200` has zero runtime effect today) or consumed somewhere not yet grepped? **Confirm which BEFORE treating it as a bug.** If unused, the "fix" differs from Early's (wire it up correctly in seconds, or remove it) rather than an `AddMinutes→AddSeconds` swap.

---

## PART B — Core datetime-UTC invariant: verify + (the rest of) document

**Invariant:** every datetime column in EVERY table is stored in UTC; timezone is SEPARATE metadata (`Event.TimeZone`), never baked into the datetime; convert IST↔UTC only at edges (display/input). See `timezone-datetime.md` — the invariant + verified-columns + rollback/serialization/IANA notes are ALREADY DOCUMENTED (done 2026-06-28); this part is the remaining **full code audit**.

**Verify in code (audit — don't assume):**
1. All datetime WRITES store UTC. No path writes a local (IST) datetime to a UTC column. (Spot-verified this session: StartTime, ReadTimeUtc, ChipTime, ManualCrossingUtc. PENDING: every other datetime column.)
2. All datetime READS that display to the user convert UTC→IST via the timezone field — never raw UTC shown as local.
3. Arithmetic (GunTime/NetTime AND the Part A cutoff windows) is UTC−UTC, no timezone math mid-calculation. (This is the seam with Part A: `gun ± cutoff` must be UTC instant ± correctly-united duration — Part A fixes the unit; B3 confirms the instant is clean UTC.)
4. Flag ANY column/path where a datetime might be stored as local-time-with-a-separate-tz-tag (ambiguous) rather than pure-UTC. That ambiguity is the bug source — confirm it doesn't exist.

**Outcome:** mostly verify-and-document, BUT any invariant violation found = its own flagged fix (label it separately from "documented the invariant").

---

## Reporting (both parts, before any code)
One research document:
- Part A: all read sites for BOTH cutoffs + consumption units; null-OR-zero path behavior; write-path units; whether 48/49 have RaceSettings rows; any other mismatched setting in the group.
- Part B: any place the UTC invariant is violated in code (latent bugs); confirmation the cutoff arithmetic operates on clean UTC.

Then get plan approval. Part A → code change + sweep results. Part B → verify-and-document; violations become separately-flagged fixes. Keep both OUT of commit 1.
