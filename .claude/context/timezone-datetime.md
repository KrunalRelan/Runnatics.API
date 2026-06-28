# Timezone & DateTime Handling

## Core rule (the invariant)
**Every datetime column in every table is stored in pure UTC. The timezone is stored SEPARATELY as metadata (e.g. `Event.TimeZone = "Asia/Kolkata"`), never baked into the datetime.** The stored datetime is a clean UTC instant; the timezone column is used ONLY to convert to/from local (IST) at the edges — user input and display. Convert IST↔UTC ONLY at those edges; store and compute in UTC.

Corollary for arithmetic: every datetime calculation is UTC − UTC. `GunTime`/`NetTime = finishUtc − startUtc`, and the load-window cutoffs are `gunUtc ± duration` — both operate on clean UTC instants. (⚠️ The *unit* of the cutoff duration is a SEPARATE, open concern — `EarlyStartCutOff` is consumed via `AddMinutes` though the column is seconds; that is the queued Part A audit, not a UTC-invariant violation. The datetime it is applied to, `Races.StartTime`, is clean UTC.)

## Storage
- RawRFIDReadings.ReadTimeUtc: true UTC. (TimeZoneId="UTC"; ReadTimeLocal == ReadTimeUtc for event 30.)
- ReadNormalized.ChipTime: the UTC crossing instant.
- Races.StartTime: UTC (the race gun, in UTC).
- GunTime / NetTime: elapsed milliseconds from the gun (chipTime - raceStartUtc). Not wall-clock.
- Event.TimeZone: "Asia/Kolkata" (confirmed event 30). Conversion source for display and input.

## Verification status (as of 2026-06-28)
The invariant above is the DESIGN RULE. What has been **verified by direct code read this session**:
- **Writes store UTC** at: `Races.StartTime` (UTC gun); `RawRFIDReading.ReadTimeUtc` (`ParseSqliteFileAsync` — UTC instant, `ReadTimeLocal` is the convenience local copy); `ReadNormalized.ChipTime` (the UTC crossing instant, Phase 2 & Phase 2.4); `ManualTimeOverride.ManualCrossingUtc` (IST→UTC converted at input via `Event.TimeZone`, `RecordManualTimeAsync`).
- **Edge conversion** uses `Event.TimeZone` for both manual-time input (IST→UTC) and split/checkpoint display (UTC→IST). Same path, no hardcoded IST branch.
- **Arithmetic is UTC−UTC** for GunTime/NetTime and for the `gun ± cutoff` load window.

**⏳ PENDING — NOT yet done:** a full, every-column / every-table audit confirming NO datetime column anywhere is stored as local-time-with-a-separate-tz-tag (the ambiguity that is the real bug source). That comprehensive sweep is the queued **Part B code audit** (see `queued-cutoff-datetime-audit.md`), gated behind the race-49 StartTime correction + commit-1 verification. Until that sweep runs, treat the invariant as **verified for the columns listed above, asserted-but-unaudited elsewhere.**

## The midnight-rollback fact (critical — caused hours of confusion)
IST = UTC + 5:30, so any IST time BEFORE 05:30 AM converts to the PREVIOUS UTC calendar day. This is CORRECT, not a bug:
- 05:00 IST = 23:30 UTC prev day | 05:29 IST = 23:59 UTC prev day | 05:30 IST = 00:00 UTC same day | 06:00 IST = 00:30 UTC same day
A pre-dawn IST gun legitimately stores on the day before. Do NOT "fix" a day-before StartTime without first checking whether the IST gun is before 05:30 AM.

## Conversion (code)
- Display (UTC→IST) and input (IST→UTC) MUST both key off Event.TimeZone via TimeZoneInfo — same path for automatic reads and manual entry. Never a hardcoded IST branch, never two parallel conversions.
- API runs on Linux App Service: TimeZoneInfo IDs must be IANA ("Asia/Kolkata"), not Windows ("India Standard Time"). Verify stored Event.TimeZone resolves on Linux.

## Serialization gotcha (caused the manual-time -24h bug)
Kind-less datetime strings (e.g. "2026-05-09T23:59:00", no Z) get parsed as BROWSER-LOCAL by JS new Date(), locking to the wrong day. Always send/parse explicit UTC (Z) or an explicit event-local value with the date attached. Manual-time editors must pre-fill DATE+time from the record's actual crossing (ChipTime→IST), never from a different source (e.g. race StartTime's date).

## Manual-time entry rule
- Editor shows full DATE + time (crossings can land on a different calendar day than the race date due to the rollback above).
- Pre-fill from the split's existing ChipTime (UTC→IST). Convert input IST→UTC on save via Event.TimeZone. Validate chipTime = finishUtc - raceStartUtc is POSITIVE (negative = wrong day/gun; keep this guard).
