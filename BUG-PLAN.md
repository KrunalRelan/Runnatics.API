# Bug Fix Plan — Round 2

> **Status:** DRAFT — Do not implement until user approves each bug's plan
> **Priority:** P0 = Blocking production, P1 = Major functionality broken, P2 = Enhancement/polish

---

## PHASE 1 — CRITICAL BUGS (Fix First)

### BUG-01: EPC Mapping — Multiple Tags Auto-Map Instead of Showing Error [P0]

**Reporter:** Punit Lakra
**Current behavior:** When 2-3 RFID tags are scanned simultaneously, the system automatically maps each tag to the next available BIB number in sequence.
**Expected behavior:** When multiple EPCs are detected in a single scan operation, the system should:
1. Flag the scan as "Multiple EPC detected"
2. Skip all detected EPCs from auto-mapping
3. Show a red badge / error indicator on the affected BIB row(s)
4. Require the operator to scan one chip at a time

**Research targets (API):**
- Find the EPC mapping endpoint (likely in a BibMapping controller/service)
- Find the method that receives scanned EPC data — check if it handles single vs. batch
- Check if there's a timestamp-based grouping or a single-request-multiple-EPC scenario
- Look at the SignalR hub if mapping is real-time via WebSocket

**Research targets (UI):**
- Find the BIB Mapping page component
- Check how scanned EPCs arrive (WebSocket event? API poll? Direct input?)
- Find where the "Mapped" / "Not mapped" badge renders
- Check if "Multiple EPC" badge/state already exists but isn't triggered correctly

**Plan template:**
```
API:
  1. [Read] BibMappingService / EpcMappingService — find the mapping method
  2. [Read] The SignalR hub or endpoint receiving raw EPC scans
  3. [Identify] Where is the "one EPC = one BIB" assumption?
  4. [Fix] Add check: if scan batch contains >1 unique EPC, reject the batch
  5. [Fix] Return a distinct error response (not a silent skip)

UI:
  1. [Read] BibMapping page component
  2. [Read] The handler for scan results
  3. [Fix] When API returns "multiple EPC" error, show red badge on affected row
  4. [Fix] Do NOT auto-advance to next BIB on error
```

---

### BUG-02: Participant Range — BIB Numbers Not Sequential [P0]

**Reporter:** Punit Lakra
**Current behavior:** When adding participants as a range, BIB numbers don't come in sequence.
**Expected behavior:** Adding a range (e.g., 1-50) should create participants with BIB numbers 1, 2, 3, ... 50 in order.

**Research targets (API):**
- Find the participant creation endpoint that handles range/bulk creation
- Check if BIBs are assigned via auto-increment, explicit assignment, or generated
- Look for any sorting or ordering logic in the insert

**Research targets (UI):**
- Find the "Add Participants" form/dialog
- Check if range input sends individual requests or a single batch request
- Verify the display sort order on the participants list page

**Plan template:**
```
API:
  1. [Read] ParticipantService — find bulk/range creation method
  2. [Identify] How BIB numbers are assigned in the loop
  3. [Fix] Ensure sequential assignment within the range
  4. [Fix] Ensure the response returns them in order

UI:
  1. [Read] Participant list component — check sort order
  2. [Fix] Ensure display is sorted by BIB number ascending
```

---

### BUG-03: Checkpoint Creation Fails with Generic Error [P0]

**Reporter:** Screenshot evidence
**Current behavior:** "Add New Checkpoint" dialog shows "Error: Error creating checkpoint." when trying to create a checkpoint named "Start" with Device "Box 01", Distance 0, Is Mandatory checked.
**Expected behavior:** Checkpoint should be created successfully, or a meaningful validation error should be shown.

**Research targets (API):**
- Find the Checkpoint creation endpoint and service
- Check validation rules (duplicate name? device already assigned? distance conflicts?)
- Check if there's a unique constraint being violated
- Look at the error handling — is the real error being swallowed?

**Research targets (UI):**
- Find the Checkpoint creation dialog component
- Check the error handling — is it catching and displaying the API error detail?
- Check if the API returns a useful error message or just 500

**Plan template:**
```
API:
  1. [Read] CheckpointService — find Create method
  2. [Read] Checkpoint entity config — check constraints
  3. [Identify] What validation or DB constraint is failing
  4. [Fix] Add proper validation with descriptive error messages
  5. [Fix] Return 400 with message, not 500

UI:
  1. [Read] Checkpoint creation dialog
  2. [Fix] Display the specific error message from API response
```

---

## PHASE 2 — RESULT ENGINE BUGS

### BUG-04: Split/Cumulative Timings Incorrect in Results [P1]

**Current behavior:** Individual split timings and cumulative timings are not correct.
**Expected behavior:**
- Split time = time at checkpoint N minus time at checkpoint N-1
- Cumulative time = time at checkpoint N minus start time
- Both should account for the correct checkpoint order by distance

**Research targets:**
- Find the result processing service
- Find the split/cumulative calculation methods
- Check checkpoint ordering logic (by distance? by sequence?)
- Check timezone handling (IST↔UTC)

---

### BUG-05: DNF/Finished Status Not Working Correctly [P1]

**Current behavior:** Participant status is not correctly set based on mandatory checkpoint detection.
**Expected behavior:**
- **Finished** = EPC detected at ALL mandatory checkpoints
- **DNF** = any mandatory checkpoint missed
- Non-mandatory checkpoints should NOT affect status

**Research targets:**
- Find the status determination logic in result processing
- Check how `IsMandatory` flag on checkpoints is used
- Verify the query filters mandatory vs optional checkpoints

---

### BUG-06: Race Category Change — Data Not Transferring [P1]

**Current behavior:** Changing a participant's race category doesn't transfer checkpoint timestamps or reprocess results.
**Expected behavior:**
- All participant details transfer to new race category
- All checkpoint timestamps transfer
- Results are reprocessed for the new category
- A "Process Result" button should exist in the BIB drill-down

**Research targets:**
- Find the race category change endpoint
- Check if checkpoint readings are category-dependent or participant-dependent
- Find the result reprocessing trigger

---

### BUG-07: Wrong Participant Showing in Wrong Race Category (7th GGHM) [P0]

**Current behavior:** In 10 KM category, "Commodore" shows as Rank 1 but their BIB belongs to 5 KM category.
**Expected behavior:** Results should only include participants belonging to that specific race category.

**Research targets:**
- This is likely related to BUG-06 or a filter bug in result queries
- Check the result query — does it filter by RaceId/RaceCategoryId?
- Check if a race category change happened without proper data migration

---

## PHASE 3 — LEADERBOARD & PUBLIC PAGE BUGS

### BUG-08: Overall Result Tab Not Working on Published Leaderboard [P1]

**Current behavior:** "Overall Result" tab doesn't function when leaderboard is published using Gun Time or Chip Time.
**Expected behavior:** Overall result should display all participants sorted by time, split into Male/Female tabs.
**Additional:** Gun Time tab in Age Category section is also broken (Chip Time works).

---

### BUG-09: Leaderboard Per Race Category [P2]

**Expected behavior:** Each race category (21.1 KM, 10 KM, 5 KM) should have its own leaderboard section.

---

### BUG-10: Public Domain — Female Results Not Displaying [P1]

**Current behavior:** Only one female participant showing on public result page.
**Expected behavior:** All female participants should display, same format as male results.

---

### BUG-11: Remove "Show All Finishers" Button [P2]

**Expected behavior:** Remove from both Overall Result and Age Category Result sections on public domain.

---

### BUG-12: Unknown Age Category Appearing [P2]

**Current behavior:** If participant has no age category, system creates "Unknown Category" on leaderboard.
**Expected behavior:** Don't create unknown category. If details incomplete, show only BIB number.

---

### BUG-13: Overall Result Display Rules [P1]

**Expected behavior:**
- If Overall Result is published, display it regardless of Age Category availability
- Only Gender is mandatory for Overall Result
- Separate Male/Female tabs
- Overall Result displayed BEFORE Age Category results

---

## PHASE 4 — UX & FEATURE BUGS

### BUG-14: Manual Time Edit Not Working [P1]

**Current behavior:** Manual time edit function is not operational.
**Expected behavior:**
- Should NOT work if EPC is not mapped (when race is Timed)
- Should auto-activate when a checkpoint is created
- Should work for all checkpoints that exist

---

### BUG-15: All Reader Detections — Show Every Timestamp [P2]

**Expected behavior:** Display every detection captured by each reader, with reader name and timestamp, including multiple detections per participant per checkpoint.

---

### BUG-16: BIB Drill-Down — Missing Columns [P2]

**Expected behavior:** Add Gender and Manual Distance columns. Manual Distance should be filterable by checkpoint.

---

### BUG-17: Gender Display — Use Capital M/F [P2]

**Expected behavior:** Display "M" and "F" instead of "male" and "female" everywhere.

---

### BUG-18: Is Timed Toggle — EPC Mapping Dependency [P2]

**Expected behavior:**
- Timed = ON → EPC mapping mandatory for that race
- Timed = OFF → EPC mapping not required

---

### BUG-19: Reader File Upload — Allow Multiple Uploads [P2]

**Current behavior:** File can only be uploaded once.
**Expected behavior:** Allow re-upload. After upload, show confirmation: "X of Y tags uploaded successfully."

---

### BUG-20: Support Query Reply Not Working [P1]

**Current behavior:** Cannot send a reply to support queries submitted via public website.
**Expected behavior:** Use existing `POST /api/support/{id}/comments` endpoint. Diagnose why it's failing.

---

### BUG-21: Right-Click "Open in New Tab" Not Working [P2]

**Current behavior:** Links don't support right-click → Open in New Tab.
**Expected behavior:** Use proper `<a href>` tags or `react-router` `<Link>` components instead of `onClick` navigation.

---

### BUG-22: Public Page Mobile Responsiveness [P2]

**Expected behavior:** Public domain pages fully responsive across all screen sizes.

---

### BUG-23: Event/Race Dashboard Pie Charts [P2]

**Expected behavior:** Pie charts showing Total Participants, Started, Finished, DNF/Not Started — both at event level and per race category.

---

## EXECUTION ORDER

```
Round 1 (P0 — Blocking):  BUG-01, BUG-02, BUG-03, BUG-07
Round 2 (P1 — Core):      BUG-04, BUG-05, BUG-06, BUG-14, BUG-20
Round 3 (P1 — Leaderboard): BUG-08, BUG-10, BUG-13
Round 4 (P2 — Polish):    BUG-09, BUG-11, BUG-12, BUG-15, BUG-16, BUG-17, BUG-18, BUG-19, BUG-21, BUG-22, BUG-23
```

**Do NOT skip rounds. Complete each round's 2-1-2 cycle before proceeding.**
