# Timezone & DateTime Handling

## Core rule
All datetime data is stored in UTC. The event runs in IST (Asia/Kolkata, UTC+5:30). Convert IST↔UTC ONLY at the edges (user input, display); store and compute in UTC.

## Storage
- RawRFIDReadings.ReadTimeUtc: true UTC. (TimeZoneId="UTC"; ReadTimeLocal == ReadTimeUtc for event 30.)
- ReadNormalized.ChipTime: the UTC crossing instant.
- Races.StartTime: UTC (the race gun, in UTC).
- GunTime / NetTime: elapsed milliseconds from the gun (chipTime - raceStartUtc). Not wall-clock.
- Event.TimeZone: "Asia/Kolkata" (confirmed event 30). Conversion source for display and input.

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
