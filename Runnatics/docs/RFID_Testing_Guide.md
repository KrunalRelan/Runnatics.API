# RFID Import and Results Processing - Testing Guide

## Overview
This guide provides step-by-step instructions for testing the complete RFID workflow for a 10KM race, from data upload to final results generation.

---

## Current Implementation Status

### ? **Implemented**
- ? Event Creation (Steps 1-4)
- ? Race Creation
- ? Checkpoint Configuration  
- ? Participant Registration with BIB numbers
- ? **EPC-BIB Mapping Upload (Step 5a)** - NEW!
- ? RFID File Upload (SQLite .db files)
- ? Parse readings from SQLite
- ? Link EPCs to Participants via ChipAssignment
- ? **Deduplication Logic (Step 6)** - NEW!
- ? Signal strength filtering (RSSI > -75 dBm)
- ? ReadNormalized table population

### ?? **Partially Implemented**
- ?? SplitTime calculation (Step 7) - Tables exist, calculation logic needed
- ?? Results calculation (Step 7) - Tables exist, calculation logic needed

### ? **Not Implemented (Step 8)**
- ? Results API endpoints
- ? Leaderboard generation
- ? Individual results display

---

## Testing Workflow - COMPLETE STEPS 5-6

### **STEP 5a: Upload EPC-BIB Mapping** ? **IMPLEMENTED**

**Endpoint**:
```http
POST /api/RFID/{eventId}/{raceId}/epc-mapping
Content-Type: multipart/form-data
Authorization: Bearer {token}
```

**Request**:
```bash
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/epc-mapping" \
  -H "Authorization: Bearer {token}" \
  -F "file=@BHOPAL EPC DATA.xlsx"
```

**Expected Response**:
```json
{
  "message": {
    "fileName": "BHOPAL EPC DATA.xlsx",
    "processedAt": "2026-01-25T10:30:00Z",
    "totalRecords": 520,
    "successCount": 520,
    "errorCount": 0,
    "notFoundBibCount": 0,
    "notFoundBibs": [],
    "errors": [],
    "status": "Completed"
  }
}
```

**What it does**:
1. ? Parses Excel file (EPC ? BIB mapping)
2. ? Finds participants by BIB number
3. ? Creates/Updates `Chip` records with EPC
4. ? Creates `ChipAssignment` records linking EPCs to participants
5. ? Returns detailed results with any errors

**Excel File Format**:
- Must have headers containing "EPC" and "BIB"
- Example:
  ```
  EPC                              | BIB
  E28069150000401232D2F51C         | 1001
  418000A9B75B                     | 1002
  ```

---

### **STEP 5b: Upload RFID Reading Files** ? **WORKING**

**Endpoint**:
```http
POST /api/RFID/{eventId}/{raceId}/import
```

**Testing Steps**:
```bash
# Upload Box15 readings for Checkpoint 1 (START - 0 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@1. 2026-01-25_0016251292ae_(box15).db" \
  -F "deviceId=Box15" \
  -F "checkpointId={checkpointStartId}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Upload Box16 readings for Checkpoint 2 (2.5 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@1.1 2026-01-25_0016251292a1_(Box-16).db" \
  -F "deviceId=Box16" \
  -F "checkpointId={checkpoint25KmId}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Upload Box19 readings for Checkpoint 3 (5 KM)  
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@2. 2026-01-25_00162512dbb0_(Box-19).db" \
  -F "deviceId=Box19" \
  -F "checkpointId={checkpoint5KmId}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Upload Box24 readings for Checkpoint 4 (7.5 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@2.1 2026-01-25_001625135f24_(box_24).db" \
  -F "deviceId=Box24" \
  -F "checkpointId={checkpoint75KmId}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"
```

**Expected Response** (per file):
```json
{
  "message": {
    "fileName": "1. 2026-01-25_0016251292ae_(box15).db",
    "importBatchId": "encrypted_id",
    "uploadedAt": "2026-01-25T10:35:00Z",
    "totalRecords": 2500,
    "validRecords": 2500,
    "invalidRecords": 0,
    "status": "Uploaded"
  }
}
```

---

### **STEP 5c: Process RFID Readings** ? **WORKING**

**Endpoint**:
```http
POST /api/RFID/{eventId}/{raceId}/import/{importBatchId}/process
```

**Request** (for each batch):
```bash
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import/{importBatchId}/process" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "{eventId}",
    "raceId": "{raceId}",
    "importBatchId": "{importBatchId}"
  }'
```

**Expected Response**:
```json
{
  "message": {
    "importBatchId": 1,
    "processedAt": "2026-01-25T10:40:00Z",
    "successCount": 2450,
    "errorCount": 50,
    "unlinkedCount": 5,
    "unlinkedEPCs": ["418000UNKNOWN1", "418000UNKNOWN2"],
    "status": "CompletedWithErrors"
  }
}
```

**What it does**:
- ? Links EPCs to participants via ChipAssignment
- ? Validates signal strength (RSSI > -75 dBm)
- ? Creates `ReadingCheckpointAssignment` records
- ? Marks weak signals as "Invalid"

---

### **STEP 6: Deduplication & Normalization** ? **IMPLEMENTED**

**Endpoint**:
```http
POST /api/RFID/{eventId}/{raceId}/deduplicate
```

**Request**:
```bash
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/deduplicate" \
  -H "Authorization: Bearer {token}"
```

**Expected Response**:
```json
{
  "message": {
    "totalRawReadings": 10000,
    "normalizedReadings": 2080,
    "duplicatesRemoved": 7920,
    "checkpointsProcessed": 4,
    "participantsProcessed": 520,
    "processingTimeMs": 1234,
    "status": "Completed"
  }
}
```

**What it does**:
1. ? Groups readings by Participant + Checkpoint
2. ? Filters duplicates within time windows
3. ? Keeps **earliest timestamp** with **strongest RSSI**
4. ? Creates `ReadNormalized` records with:
   - ChipTime (exact read time)
   - GunTime (ms from race start)
   - NetTime (ms from participant start - if available)
5. ? Links to original RawRFIDReading record

**Algorithm**:
```
For each Participant-Checkpoint pair:
  1. Get all successful raw readings
  2. Sort by: Timestamp ASC, RSSI DESC
  3. Keep first reading (earliest + strongest signal)
  4. Calculate GunTime = ReadTime - RaceStartTime
  5. Insert into ReadNormalized table
```

---

## Database Verification Queries

After each step, verify data in the database:

```sql
-- After Step 5a: Check chip assignments
SELECT 
    ca.EventId,
    COUNT(*) as TotalAssignments,
    COUNT(DISTINCT ca.ParticipantId) as UniqueParticipants,
    COUNT(DISTINCT ca.ChipId) as UniqueChips
FROM ChipAssignments ca
WHERE ca.EventId = {eventId} 
  AND ca.UnassignedAt IS NULL
GROUP BY ca.EventId;

-- After Step 5b: Check raw readings per batch
SELECT 
    BatchId,
    DeviceId,
    COUNT(*) as TotalReadings,
    COUNT(DISTINCT Epc) as UniqueEPCs,
    MIN(TimestampMs) as FirstReading,
    MAX(TimestampMs) as LastReading,
    Status
FROM RawRFIDReading
WHERE BatchId IN ({batchIds})
GROUP BY BatchId, DeviceId, Status;

-- After Step 5c: Check processed readings
SELECT 
    ProcessResult,
    COUNT(*) as Count,
    AVG(CAST(RssiDbm as FLOAT)) as AvgRSSI
FROM RawRFIDReading
WHERE BatchId IN ({batchIds})
GROUP BY ProcessResult;

-- After Step 6: Check normalized readings
SELECT 
    c.Name as CheckpointName,
    COUNT(DISTINCT rn.ParticipantId) as UniqueParticipants,
    COUNT(*) as TotalReadings,
    MIN(rn.GunTime) as FastestGunTime,
    MAX(rn.GunTime) as SlowestGunTime
FROM ReadNormalized rn
INNER JOIN Checkpoints c ON rn.CheckpointId = c.Id
WHERE rn.EventId = {eventId}
GROUP BY c.Name, c.DistanceFromStart
ORDER BY c.DistanceFromStart;

-- Check for duplicates (should be 0 after deduplication)
SELECT 
    ParticipantId,
    CheckpointId,
    COUNT(*) as ReadCount
FROM ReadNormalized
WHERE EventId = {eventId}
GROUP BY ParticipantId, CheckpointId
HAVING COUNT(*) > 1;
```

---

## Complete Testing Script

```bash
#!/bin/bash

# Configuration
API_URL="https://localhost:7001/api"
EVENT_ID="{your_event_id}"
RACE_ID="{your_race_id}"
TOKEN="{your_auth_token}"

echo "=== RFID Import Testing Script ==="
echo ""

# Step 5a: Upload EPC mapping
echo "Step 5a: Uploading EPC-BIB mapping..."
EPC_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/epc-mapping" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@BHOPAL EPC DATA.xlsx")
echo "$EPC_RESPONSE" | jq '.'
echo ""

# Step 5b & 5c: Upload and process RFID files
declare -A FILES=(
  ["Box15"]="1. 2026-01-25_0016251292ae_(box15).db|{checkpointStartId}"
  ["Box16"]="1.1 2026-01-25_0016251292a1_(Box-16).db|{checkpoint25KmId}"
  ["Box19"]="2. 2026-01-25_00162512dbb0_(Box-19).db|{checkpoint5KmId}"
  ["Box24"]="2.1 2026-01-25_001625135f24_(box_24).db|{checkpoint75KmId}"
)

BATCH_IDS=()

for DEVICE in "${!FILES[@]}"; do
  IFS='|' read -r FILE CHECKPOINT <<< "${FILES[$DEVICE]}"
  
  echo "Step 5b: Uploading $FILE..."
  UPLOAD_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/import" \
    -H "Authorization: Bearer $TOKEN" \
    -F "file=@$FILE" \
    -F "deviceId=$DEVICE" \
    -F "checkpointId=$CHECKPOINT" \
    -F "timeZoneId=Asia/Kolkata" \
    -F "treatAsUtc=false")
  
  BATCH_ID=$(echo "$UPLOAD_RESPONSE" | jq -r '.message.importBatchId')
  BATCH_IDS+=("$BATCH_ID")
  echo "Batch ID: $BATCH_ID"
  
  echo "Step 5c: Processing batch $BATCH_ID..."
  PROCESS_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/import/$BATCH_ID/process" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"eventId\":\"$EVENT_ID\",\"raceId\":\"$RACE_ID\",\"importBatchId\":\"$BATCH_ID\"}")
  echo "$PROCESS_RESPONSE" | jq '.'
  echo ""
done

# Step 6: Deduplicate
echo "Step 6: Deduplicating readings..."
DEDUP_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/deduplicate" \
  -H "Authorization: Bearer $TOKEN")
echo "$DEDUP_RESPONSE" | jq '.'
echo ""

echo "=== Testing Complete ==="
echo "Batch IDs: ${BATCH_IDS[@]}"
```

---

## Next Steps (Remaining Implementation)

### **Phase 2: Results Calculation** (Not Yet Implemented)

**Still needed**:

1. **Split Time Calculation Service**
   - Calculate time at each checkpoint
   - Calculate segment times between checkpoints
   - Calculate pace (min/km)
   - Store in `SplitTimes` table

2. **Results Calculation Service**
   - Identify finishers (crossed FINISH checkpoint)
   - Calculate final times (GunTime, NetTime)
   - Rank participants (Overall, Gender, Category)
   - Store in `Results` table

3. **Results API Endpoints**
   - GET /api/Results/{eventId}/{raceId}/leaderboard
   - GET /api/Results/{eventId}/{raceId}/participant/{participantId}
   - POST /api/Results/{eventId}/{raceId}/calculate

---

## Summary of Implemented Features

### ? **What's Now Working**:

1. **EPC-BIB Mapping Upload** (Step 5a)
   - Upload Excel file with EPC ? BIB mappings
   - Automatically creates Chip and ChipAssignment records
   - Links EPCs to participants

2. **RFID File Upload** (Step 5b)
   - Upload SQLite .db files from RFID readers
   - Parse readings from `tags` table
   - Store in `RawRFIDReading` table

3. **Reading Processing** (Step 5c)
   - Link readings to participants via EPC
   - Validate signal strength (RSSI filtering)
   - Create checkpoint assignments

4. **Deduplication** (Step 6)
   - Group by participant + checkpoint
   - Keep earliest reading with strongest RSSI
   - Populate `ReadNormalized` table
   - Calculate GunTime from race start

### ?? **Data Flow**:
```
Excel File (EPC-BIB) ? Chips + ChipAssignments
SQLite Files (.db) ? RawRFIDReading + ReadingCheckpointAssignment
Deduplication ? ReadNormalized
(Next: SplitTimes ? Results)
```

---

## Troubleshooting

### Common Issues:

1. **"No participant found with this RFID tag"**
   - Ensure Step 5a (EPC mapping) was completed first
   - Verify BIB numbers in Excel match participant records

2. **"Weak signal (RSSI < -75 dBm)"**
   - Normal - weak signals are filtered out automatically
   - Check reader placement if too many readings are weak

3. **High duplicate count in deduplication**
   - Normal - readers scan tags multiple times per second
   - Deduplication keeps only the best reading

4. **Missing checkpoints in normalized data**
   - Verify checkpoint IDs were provided during upload
   - Check if participants actually crossed that checkpoint

---

## Next Immediate Action

To complete the workflow, implement:

1. **Split Time Calculation** (6-8 hours)
2. **Results Calculation** (6-8 hours)  
3. **Results Display APIs** (8-10 hours)

**Estimated total**: 20-26 hours to complete full workflow.


---

## Data Files Analysis

Based on your sample data:

### 1. **Checkpoint Configuration** (from image)
```
START (0 KM) ? CP1 (2.5 KM) ? CP2 (5 KM) ? CP3 (7.5 KM) ? FINISH (10 KM)
```

### 2. **RFID Device Files**
- `1. 2026-01-25_0016251292ae_(box15).db` - Device Box15
- `1.1 2026-01-25_0016251292a1_(Box-16).db` - Device Box16  
- `2. 2026-01-25_00162512dbb0_(Box-19).db` - Device Box19
- `2.1 2026-01-25_001625135f24_(box_24).db` - Device Box24

**SQLite Schema**: 
```sql
CREATE TABLE tags (
    id INTEGER PRIMARY KEY, 
    epc TEXT, 
    time INTEGER, 
    antenna INTEGER, 
    rssi REAL, 
    channel INTEGER
)
```

### 3. **Participants Data** (`10km participants data.csv`)
- 520 participants (BIB 1001-1194, 1301-1520, 1601-1702)
- Fields: `bib`, `first_name`, `email`, `gender`, `mobile`, `age_category`

### 4. **EPC Mapping** (`BHOPAL EPC DATA.xlsx`)
- Maps EPC codes to BIB numbers
- **Critical**: This mapping is REQUIRED before processing RFID readings

---

## Testing Workflow

### **STEP 5: Upload RFID Data**

#### 5a. Upload EPC-BIB Mapping ?? **NEW ENDPOINT NEEDED**

**Current Gap**: The application needs an endpoint to upload the EPC-BIB mapping Excel file.

**Endpoint to Create**:
```http
POST /api/RFID/{eventId}/{raceId}/epc-mapping
Content-Type: multipart/form-data

{
  "file": "BHOPAL EPC DATA.xlsx"
}
```

**What it should do**:
1. Parse Excel file (EPC ? BIB mapping)
2. Find participants by BIB number
3. Create/Update `ChipAssignment` records
4. Link EPCs to participants

**Testing Steps**:
```bash
# 1. Upload EPC mapping
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/epc-mapping" \
  -H "Authorization: Bearer {token}" \
  -F "file=@BHOPAL EPC DATA.xlsx"

# Expected Response:
{
  "message": {
    "totalRecords": 520,
    "successCount": 520,
    "errorCount": 0,
    "notFoundBibs": []
  }
}
```

#### 5b. Upload RFID Reading Files ? **WORKING**

**Endpoint**:
```http
POST /api/RFID/{eventId}/{raceId}/import
```

**Testing Steps**:
```bash
# Upload Box15 readings
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@1. 2026-01-25_0016251292ae_(box15).db" \
  -F "deviceId=Box15" \
  -F "checkpointId={checkpoint1Id}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Repeat for Box16, Box19, Box24
```

**Expected Response**:
```json
{
  "message": {
    "fileName": "1. 2026-01-25_0016251292ae_(box15).db",
    "importBatchId": "encrypted_id",
    "totalRecords": 2500,
    "validRecords": 2500,
    "invalidRecords": 0,
    "status": "Uploaded"
  }
}
```

#### 5c. Process RFID Readings ? **WORKING** (but limited)

```bash
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import/{importBatchId}/process" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "{eventId}",
    "raceId": "{raceId}",
    "importBatchId": "{importBatchId}"
  }'
```

**What it currently does**:
- ? Links EPCs to participants
- ? Validates signal strength (RSSI > -75 dBm)
- ? Creates `ReadingCheckpointAssignment` records
- ? Does NOT deduplicate readings
- ? Does NOT create `ReadNormalized` records
- ? Does NOT calculate split times

---

### **STEP 6: Deduplication & Normalization** ? **NOT IMPLEMENTED**

**What's needed**: A new service method to:

1. **Group readings** by Participant + Checkpoint
2. **Filter duplicates**:
   - If multiple readings exist within a 5-second window
   - Keep the **earliest timestamp** with **strongest RSSI**
3. **Create `ReadNormalized` records**:
   - Link to `RawRFIDReading`
   - Calculate GunTime (ms from race start)
   - Calculate NetTime (ms from participant's start)

**Recommended Implementation**:

```csharp
// New service method
Task<DeduplicationResponse> DeduplicateAndNormalizeAsync(string eventId, string raceId);
```

**Algorithm**:
```sql
-- Pseudocode for deduplication
WITH RankedReadings AS (
  SELECT 
    r.*,
    ca.CheckpointId,
    ROW_NUMBER() OVER (
      PARTITION BY r.Epc, ca.CheckpointId 
      ORDER BY r.TimestampMs ASC, r.RssiDbm DESC
    ) AS rn
  FROM RawRFIDReading r
  INNER JOIN ReadingCheckpointAssignment ca ON r.Id = ca.ReadingId
  INNER JOIN ChipAssignment chip ON r.Epc = chip.Chip.EPC
  WHERE r.ProcessResult = 'Success'
)
INSERT INTO ReadNormalized (ParticipantId, CheckpointId, RawReadId, ChipTime, GunTime)
SELECT 
  chip.ParticipantId,
  rr.CheckpointId,
  rr.Id,
  rr.ReadTimeUtc,
  (rr.TimestampMs - race.StartTime) AS GunTime
FROM RankedReadings rr
WHERE rr.rn = 1 -- Keep only first (earliest) reading per participant-checkpoint
```

**Testing**:
```bash
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/deduplicate" \
  -H "Authorization: Bearer {token}"
```

**Expected Response**:
```json
{
  "message": {
    "totalRawReadings": 10000,
    "normalizedReadings": 2080,
    "duplicatesRemoved": 7920,
    "processingTimeMs": 1234
  }
}
```

---

### **STEP 7: Calculate Split Times & Results** ? **NOT IMPLEMENTED**

#### 7a. Calculate Split Times

**What's needed**: Process `ReadNormalized` records to create `SplitTime` entries.

**Endpoint to Create**:
```http
POST /api/Results/{eventId}/{raceId}/calculate-splits
```

**What it should do**:
```csharp
// For each participant:
// 1. Get all ReadNormalized records ordered by checkpoint distance
// 2. Calculate:
//    - SplitTimeMs: Time from START to this checkpoint
//    - SegmentTime: Time from previous checkpoint to this checkpoint
//    - Pace: Minutes per KM
// 3. Rank participants at each checkpoint
```

**Testing**:
```bash
curl -X POST "https://localhost:7001/api/Results/{eventId}/{raceId}/calculate-splits" \
  -H "Authorization: Bearer {token}"
```

#### 7b. Calculate Final Results

**Endpoint to Create**:
```http
POST /api/Results/{eventId}/{raceId}/calculate-final
```

**What it should do**:
1. Find participants who crossed the FINISH checkpoint
2. Calculate final times (GunTime, NetTime)
3. Rank by finish time (Overall, Gender, Category)
4. Insert/Update `Results` table

**Testing**:
```bash
curl -X POST "https://localhost:7001/api/Results/{eventId}/{raceId}/calculate-final" \
  -H "Authorization: Bearer {token}"
```

**Expected Response**:
```json
{
  "message": {
    "totalParticipants": 520,
    "finishers": 487,
    "dnf": 33,
    "disqualified": 0
  }
}
```

---

### **STEP 8: View Results** ? **NOT IMPLEMENTED**

#### 8a. Get Leaderboard

**Endpoint to Create**:
```http
GET /api/Results/{eventId}/{raceId}/leaderboard
Query Parameters:
  - rankBy: "overall" | "gender" | "category"
  - gender: "Male" | "Female" | "Others"
  - category: "15 to Below 31" | "31 to Below 46" | "46 & Above"
  - page: 1
  - pageSize: 50
```

**Response**:
```json
{
  "message": {
    "totalCount": 487,
    "page": 1,
    "pageSize": 50,
    "results": [
      {
        "rank": 1,
        "bib": "1042",
        "name": "SARVAGYA KUSHWAHA",
        "gender": "Male",
        "category": "15 to Below 31",
        "finishTime": "00:35:42",
        "gunTime": "00:35:42",
        "netTime": "00:35:40",
        "pace": "3:34 min/km",
        "splits": [
          { "checkpoint": "START", "time": "00:00:00" },
          { "checkpoint": "CP1", "time": "00:08:52", "segment": "00:08:52" },
          { "checkpoint": "CP2", "time": "00:17:48", "segment": "00:08:56" },
          { "checkpoint": "CP3", "time": "00:26:44", "segment": "00:08:56" },
          { "checkpoint": "FINISH", "time": "00:35:42", "segment": "00:08:58" }
        ]
      }
    ]
  }
}
```

#### 8b. Get Individual Participant Result

**Endpoint to Create**:
```http
GET /api/Results/{eventId}/{raceId}/participant/{participantId}
```

**Response**:
```json
{
  "message": {
    "participant": {
      "bib": "1042",
      "name": "SARVAGYA KUSHWAHA",
      "email": "sarvagyakushwaha06@gmail.com"
    },
    "result": {
      "overallRank": 1,
      "genderRank": 1,
      "categoryRank": 1,
      "finishTime": "00:35:42",
      "status": "Finished"
    },
    "splits": [ /* checkpoint times */ ]
  }
}
```

---

## Implementation Priority

### **Phase 1: Critical Missing Features** (Required for basic functionality)

1. **EPC-BIB Mapping Upload** ? **HIGHEST PRIORITY**
   - Create endpoint + service method
   - Parse Excel, link to ChipAssignment table
   - **Estimated effort**: 4-6 hours

2. **Deduplication & Normalization** ?
   - Implement earliest reading + strongest RSSI logic
   - Populate `ReadNormalized` table
   - **Estimated effort**: 8-10 hours

3. **Split Time Calculation** ?
   - Process normalized readings
   - Populate `SplitTime` table
   - **Estimated effort**: 6-8 hours

### **Phase 2: Results & Display**

4. **Results Calculation**
   - Calculate final times and rankings
   - Populate `Results` table
   - **Estimated effort**: 6-8 hours

5. **Results API Endpoints**
   - Leaderboard
   - Individual results
   - Export functionality
   - **Estimated effort**: 8-10 hours

---

## Quick Test Script (Once Implemented)

```bash
#!/bin/bash

# Configuration
API_URL="https://localhost:7001/api"
EVENT_ID="{your_event_id}"
RACE_ID="{your_race_id}"
TOKEN="{your_auth_token}"

# Step 5a: Upload EPC mapping
echo "Uploading EPC-BIB mapping..."
curl -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/epc-mapping" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@BHOPAL EPC DATA.xlsx"

# Step 5b: Upload RFID files
echo "Uploading RFID readings..."
for FILE in *.db; do
  DEVICE_ID=$(echo $FILE | cut -d'_' -f3 | cut -d'.' -f1)
  curl -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/import" \
    -H "Authorization: Bearer $TOKEN" \
    -F "file=@$FILE" \
    -F "deviceId=$DEVICE_ID" \
    -F "timeZoneId=Asia/Kolkata"
done

# Step 6: Deduplicate
echo "Deduplicating readings..."
curl -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/deduplicate" \
  -H "Authorization: Bearer $TOKEN"

# Step 7: Calculate results
echo "Calculating split times..."
curl -X POST "$API_URL/Results/$EVENT_ID/$RACE_ID/calculate-splits" \
  -H "Authorization: Bearer $TOKEN"

echo "Calculating final results..."
curl -X POST "$API_URL/Results/$EVENT_ID/$RACE_ID/calculate-final" \
  -H "Authorization: Bearer $TOKEN"

# Step 8: View results
echo "Fetching leaderboard..."
curl -X GET "$API_URL/Results/$EVENT_ID/$RACE_ID/leaderboard?page=1&pageSize=10" \
  -H "Authorization: Bearer $TOKEN"
```

---

## Database Verification Queries

After each step, verify data in the database:

```sql
-- Check chip assignments (after Step 5a)
SELECT COUNT(*) FROM ChipAssignments WHERE ParticipantId IS NOT NULL;

-- Check raw readings (after Step 5b)
SELECT BatchId, COUNT(*), MIN(TimestampMs), MAX(TimestampMs) 
FROM RawRFIDReading 
GROUP BY BatchId;

-- Check normalized readings (after Step 6)
SELECT COUNT(*) FROM ReadNormalized;

-- Check split times (after Step 7a)
SELECT CheckpointId, COUNT(*) FROM SplitTimes GROUP BY CheckpointId;

-- Check final results (after Step 7b)
SELECT Status, COUNT(*) FROM Results GROUP BY Status;
```

---

## Summary

**What's Working**:
- ? Steps 1-4 (Event ? Race ? Checkpoints ? Participants)
- ? RFID file upload (Step 5b)
- ? Basic EPC-Participant linking (Step 5c)

**What's Missing**:
- ? EPC-BIB mapping upload (Step 5a) - **CRITICAL**
- ? Deduplication logic (Step 6)
- ? Split time calculation (Step 7)
- ? Results calculation (Step 7)
- ? Results display (Step 8)

**Next Immediate Action**:
1. Implement EPC-BIB mapping upload endpoint
2. Enhance ProcessRFIDStagingDataAsync to include deduplication
3. Create Results calculation service
