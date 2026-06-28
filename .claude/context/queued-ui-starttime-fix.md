# QUEUED (UI repo) — edit-race form must send the correct UTC StartTime on every save

**Status:** QUEUED for the **UI repo** (`Runnatics.UI` / `Runnatics.Ui`), gated on the auth/push block like the other UI work. **No API change** — the server is intentionally permissive and non-corrupting (see below).

## The bug (correctly framed)
Race start/end time is a **normal, freely-editable field** — users must be able to change a race's gun anytime via the edit-race screen. The problem is NOT that the gun can change; it's that the gun **changed when the user didn't intend to change it**:

- The edit-race / RaceSettings modal sends the **whole `RaceRequest`** (one DTO, one `PUT .../edit-race` endpoint — there is no settings-only endpoint).
- On a **settings-only save** (user never touched the start time), the form carried a **wrong `StartTime`** — a defaulted / mis-deserialized value, observed as `2026-05-09 23:59` (= `Event.EventDate`, = the 21K gun) overwriting race 49's correct `00:59`. Timestamped `2026-06-23 09:32`.
- Root cause = the **kind-less-datetime serialization trap** already documented in `timezone-datetime.md`: a `Z`-less `00:59` parsed by JS `new Date()` as browser-local, and/or a blank field defaulting to the event date → the form re-emits the wrong instant on save.

## Server is permissive AND non-corrupting (confirmed 2026-06-28, do NOT change)
- `Race.StartTime`/`EndTime` are written only via AutoMapper `CreateMap<RaceRequest, Race>()` on create (`RaceService.cs:110`) and update (`:436`). No value converter, no IST re-localization — the incoming `DateTime` is stored **as-is**. A correct UTC value in → correct UTC stored.
- This is the desired behavior: the gun stays freely editable; the server just persists whatever (correct) instant the client sends. **No `Ignore()`, no dedicated endpoint, no guard** — an earlier server lock-down was implemented and **reverted** (it wrongly restricted legitimate edits). See session-log 2026-06-28.

## The fix (UI repo)
In the edit-race form / race-settings modal:
1. **Load** `StartTime`/`EndTime` into the form as **explicit UTC** — parse the API value as UTC (ensure a `Z`/offset; never let `new Date()` interpret a kind-less string as browser-local). Show in event-local (IST) for display via `Event.TimeZone`, but keep the underlying value UTC-correct.
2. **Send** the race's **actual** `StartTime`/`EndTime` (as explicit UTC) on **every** save — including settings-only saves where the user didn't touch the gun. The unchanged correct value round-trips instead of a default.
3. **Never** default `StartTime` to `Event.EventDate` (or any placeholder) when the field is blank/unloaded. If the value isn't loaded, don't emit a fabricated one.
4. When the user **does** edit the start/end time, send the new value (normal edit — fully supported).

## Verify
- Edit only RaceSettings (don't touch start time) → save → race gun is **unchanged** (the race-49 regression: `00:59` stays `00:59`).
- Edit the start time deliberately → save → new gun persists.
- Round-trip: API UTC → form (display IST) → save → API UTC identical (no day-shift, no EventDate fallback).

## Immediate race-49 unblock (ops)
Server is permissive, so re-correct race 49 `StartTime` to `00:59` (SQL or the edit screen). **Until the UI fix lands**, avoid re-saving race-49 settings through the buggy form (it can re-clobber the gun). After the UI fix, settings saves no longer carry a wrong gun.
