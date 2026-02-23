# Complete RFID Workflow - Final Testing Guide

## ?? Implementation Complete!

All features from Steps 1-8 are now implemented. This guide provides complete end-to-end testing instructions.

---

## ?? **Complete Feature List**

### ? **Steps 1-4: Event Setup** (Already Working)
- ? Event Creation
- ? Race Creation
- ? Checkpoint Configuration
- ? Participant Registration with BIB numbers

### ? **Step 5: RFID Data Upload** (Implemented)
- ? EPC-BIB Mapping Upload
- ? RFID Reading Files Upload (SQLite .db)
- ? Reading Processing & Validation

### ? **Step 6: Deduplication** (Implemented)
- ? Group readings by participant + checkpoint
- ? Keep earliest reading with strongest RSSI
- ? Populate `ReadNormalized` table

### ? **Step 7: Results Calculation** (Implemented)
- ? Split Time Calculation
- ? Segment Time Calculation
- ? Pace Calculation (min/km)
- ? Final Results Calculation
- ? Rankings (Overall, Gender, Category)

### ? **Step 8: Results Display** (Implemented)
- ? Leaderboard API
- ? Individual Participant Results
- ? Filtering (Gender, Category)
- ? Pagination

---

## ?? **Complete Testing Workflow**

### **Prerequisites**
1. Event created with ID: `{eventId}`
2. Race created with ID: `{raceId}`
3. Checkpoints configured:
   - START (0 KM) - `{cpStartId}`
   - CP1 (2.5 KM) - `{cp25Id}`
   - CP2 (5 KM) - `{cp5Id}`
   - CP3 (7.5 KM) - `{cp75Id}`
   - FINISH (10 KM) - `{cpFinishId}`
4. Participants uploaded from CSV (520 participants, BIB 1001-1520)
5. Auth token: `{token}`

---

### **STEP 5a: Upload EPC-BIB Mapping** ?

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
    "processedAt": "2026-01-27T10:00:00Z",
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
- Parses Excel with EPC ? BIB mappings
- Creates `Chip` records for each EPC
- Creates `ChipAssignment` linking chips to participants

---

### **STEP 5b: Upload RFID Reading Files** ?

Upload all 4 SQLite files:

```bash
# Box15 - START (0 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@1. 2026-01-25_0016251292ae_(box15).db" \
  -F "deviceId=Box15" \
  -F "checkpointId={cpStartId}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Box16 - CP1 (2.5 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@1.1 2026-01-25_0016251292a1_(Box-16).db" \
  -F "deviceId=Box16" \
  -F "checkpointId={cp25Id}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Box19 - CP2 (5 KM)
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@2. 2026-01-25_00162512dbb0_(Box-19).db" \
  -F "deviceId=Box19" \
  -F "checkpointId={cp5Id}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"

# Box24 - CP3 (7.5 KM)  
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "file=@2.1 2026-01-25_001625135f24_(box_24).db" \
  -F "deviceId=Box24" \
  -F "checkpointId={cp75Id}" \
  -F "timeZoneId=Asia/Kolkata" \
  -F "treatAsUtc=false"
```

**Save the `importBatchId` from each response!**

**Expected Response** (per file):
```json
{
  "message": {
    "fileName": "1. 2026-01-25_0016251292ae_(box15).db",
    "importBatchId": "encrypted_batch_id",
    "uploadedAt": "2026-01-27T10:05:00Z",
    "totalRecords": 2500,
    "validRecords": 2500,
    "invalidRecords": 0,
    "status": "Uploaded"
  }
}
```

---

### **STEP 5c: Process Each Batch** ?

Process each uploaded batch:

```bash
# Process Box15 batch
curl -X POST "https://localhost:7001/api/RFID/{eventId}/{raceId}/import/{batchId1}/process" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "{eventId}",
    "raceId": "{raceId}",
    "importBatchId": "{batchId1}"
  }'

# Repeat for batchId2, batchId3, batchId4
```

**Expected Response**:
```json
{
  "message": {
    "importBatchId": 1,
    "processedAt": "2026-01-27T10:10:00Z",
    "successCount": 2450,
    "errorCount": 50,
    "unlinkedCount": 5,
    "unlinkedEPCs": ["418000UNKNOWN1"],
    "status": "CompletedWithErrors"
  }
}
```

---

### **STEP 6: Deduplicate Readings** ?

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
    "checkpointsProcessed": 5,
    "participantsProcessed": 520,
    "processingTimeMs": 1234,
    "status": "Completed"
  }
}
```

**What it does**:
- Groups readings by participant + checkpoint
- Keeps earliest reading with strongest RSSI
- Creates `ReadNormalized` records with ChipTime and GunTime

---

### **STEP 7a: Calculate Split Times** ? **NEW!**

```bash
curl -X POST "https://localhost:7001/api/Results/{eventId}/{raceId}/calculate-splits" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "{eventId}",
    "raceId": "{raceId}",
    "forceRecalculation": false
  }'
```

**Expected Response**:
```json
{
  "message": {
    "totalParticipants": 520,
    "participantsWithSplits": 487,
    "totalSplitTimesCreated": 2435,
    "checkpointsProcessed": 5,
    "processingTimeMs": 2345,
    "status": "Completed",
    "checkpointSummaries": [
      {
        "checkpointId": "encrypted_id",
        "checkpointName": "START",
        "distanceKm": 0,
        "participantCount": 520,
        "fastestTimeMs": 0,
        "slowestTimeMs": 0,
        "fastestTimeFormatted": "00:00:00",
        "slowestTimeFormatted": "00:00:00"
      },
      {
        "checkpointId": "encrypted_id",
        "checkpointName": "CP1 2.5KM",
        "distanceKm": 2.5,
        "participantCount": 510,
        "fastestTimeMs": 532000,
        "slowestTimeMs": 1020000,
        "fastestTimeFormatted": "00:08:52",
        "slowestTimeFormatted": "00:17:00"
      },
      {
        "checkpointId": "encrypted_id",
        "checkpointName": "FINISH",
        "distanceKm": 10,
        "participantCount": 487,
        "fastestTimeMs": 2142000,
        "slowestTimeMs": 4680000,
        "fastestTimeFormatted": "00:35:42",
        "slowestTimeFormatted": "01:18:00"
      }
    ]
  }
}
```

**What it does**:
- Processes `ReadNormalized` records
- Calculates split times at each checkpoint
- Calculates segment times between checkpoints
- Calculates pace (min/km)
- Calculates rankings (Overall, Gender, Category)
- Stores in `SplitTimes` table

---

### **STEP 7b: Calculate Final Results** ? **NEW!**

```bash
curl -X POST "https://localhost:7001/api/Results/{eventId}/{raceId}/calculate-results" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "eventId": "{eventId}",
    "raceId": "{raceId}",
    "forceRecalculation": false,
    "markAsOfficial": false
  }'
```

**Expected Response**:
```json
{
  "message": {
    "totalParticipants": 520,
    "finishers": 487,
    "dnf": 33,
    "disqualified": 0,
    "fastestFinishTimeMs": 2142000,
    "slowestFinishTimeMs": 4680000,
    "fastestFinishTimeFormatted": "00:35:42",
    "slowestFinishTimeFormatted": "01:18:00",
    "processingTimeMs": 1567,
    "status": "Completed"
  }
}
```

**What it does**:
- Identifies finishers (crossed FINISH checkpoint)
- Calculates final times (GunTime, NetTime)
- Marks DNF participants
- Calculates rankings (Overall, Gender, Category)
- Stores in `Results` table

---

### **STEP 8a: Get Leaderboard** ? **NEW!**

**Overall Leaderboard**:
```bash
curl -X GET "https://localhost:7001/api/Results/{eventId}/{raceId}/leaderboard?rankBy=overall&page=1&pageSize=10" \
  -H "Authorization: Bearer {token}"
```

**Gender Leaderboard** (Male):
```bash
curl -X GET "https://localhost:7001/api/Results/{eventId}/{raceId}/leaderboard?rankBy=gender&gender=Male&page=1&pageSize=10" \
  -H "Authorization: Bearer {token}"
```

**Category Leaderboard**:
```bash
curl -X GET "https://localhost:7001/api/Results/{eventId}/{raceId}/leaderboard?rankBy=category&category=15%20to%20Below%2031&page=1&pageSize=10" \
  -H "Authorization: Bearer {token}"
```

**With Split Times**:
```bash
curl -X GET "https://localhost:7001/api/Results/{eventId}/{raceId}/leaderboard?rankBy=overall&page=1&pageSize=10&includeSplits=true" \
  -H "Authorization: Bearer {token}"
```

**Expected Response**:
```json
{
  "message": {
    "totalCount": 487,
    "page": 1,
    "pageSize": 10,
    "totalPages": 49,
    "rankBy": "overall",
    "gender": null,
    "category": null,
    "results": [
      {
        "rank": 1,
        "participantId": "encrypted_id",
        "bib": "1042",
        "firstName": "SARVAGYA",
        "lastName": "KUSHWAHA",
        "fullName": "SARVAGYA KUSHWAHA",
        "gender": "Male",
        "category": "15 to Below 31",
        "age": 18,
        "city": "Bhopal",
        "finishTimeMs": 2142000,
        "gunTimeMs": 2142000,
        "netTimeMs": 2140000,
        "finishTime": "00:35:42",
        "gunTime": "00:35:42",
        "netTime": "00:35:40",
        "overallRank": 1,
        "genderRank": 1,
        "categoryRank": 1,
        "averagePace": 3.57,
        "averagePaceFormatted": "3:34 min/km",
        "status": "Finished",
        "splits": [
          {
            "checkpointId": "encrypted_id",
            "checkpointName": "START",
            "distanceKm": 0,
            "splitTimeMs": 0,
            "segmentTimeMs": null,
            "splitTime": "00:00:00",
            "segmentTime": null,
            "pace": null,
            "paceFormatted": null,
            "rank": 1,
            "genderRank": 1,
            "categoryRank": 1
          },
          {
            "checkpointId": "encrypted_id",
            "checkpointName": "CP1 2.5KM",
            "distanceKm": 2.5,
            "splitTimeMs": 532000,
            "segmentTimeMs": 532000,
            "splitTime": "00:08:52",
            "segmentTime": "00:08:52",
            "pace": 3.55,
            "paceFormatted": "3:33 min/km",
            "rank": 1,
            "genderRank": 1,
            "categoryRank": 1
          },
          {
            "checkpointId": "encrypted_id",
            "checkpointName": "FINISH",
            "distanceKm": 10,
            "splitTimeMs": 2142000,
            "segmentTimeMs": 538000,
            "splitTime": "00:35:42",
            "segmentTime": "00:08:58",
            "pace": 3.57,
            "paceFormatted": "3:34 min/km",
            "rank": 1,
            "genderRank": 1,
            "categoryRank": 1
          }
        ]
      }
    ]
  }
}
```

---

### **STEP 8b: Get Individual Participant Result** ? **NEW!**

```bash
curl -X GET "https://localhost:7001/api/Results/{eventId}/{raceId}/participant/{participantId}" \
  -H "Authorization: Bearer {token}"
```

**Expected Response**:
```json
{
  "message": {
    "participant": {
      "participantId": "encrypted_id",
      "bib": "1042",
      "firstName": "SARVAGYA",
      "lastName": "KUSHWAHA",
      "fullName": "SARVAGYA KUSHWAHA",
      "email": "sarvagyakushwaha06@gmail.com",
      "phone": "7354379774",
      "gender": "Male",
      "category": "15 to Below 31",
      "age": 18,
      "city": "Bhopal",
      "state": "Madhya Pradesh"
    },
    "result": {
      "resultId": "encrypted_id",
      "finishTimeMs": 2142000,
      "gunTimeMs": 2142000,
      "netTimeMs": 2140000,
      "finishTime": "00:35:42",
      "gunTime": "00:35:42",
      "netTime": "00:35:40",
      "overallRank": 1,
      "genderRank": 1,
      "categoryRank": 1,
      "averagePace": 3.57,
      "averagePaceFormatted": "3:34 min/km",
      "status": "Finished",
      "disqualificationReason": null,
      "isOfficial": false,
      "certificateGenerated": false
    },
    "splits": [
      {
        "checkpointId": "encrypted_id",
        "checkpointName": "START",
        "distanceKm": 0,
        "splitTimeMs": 0,
        "segmentTimeMs": null,
        "splitTime": "00:00:00",
        "segmentTime": null,
        "pace": null,
        "paceFormatted": null,
        "rank": 1,
        "genderRank": 1,
        "categoryRank": 1
      },
      {
        "checkpointId": "encrypted_id",
        "checkpointName": "CP1 2.5KM",
        "distanceKm": 2.5,
        "splitTimeMs": 532000,
        "segmentTimeMs": 532000,
        "splitTime": "00:08:52",
        "segmentTime": "00:08:52",
        "pace": 3.55,
        "paceFormatted": "3:33 min/km",
        "rank": 1,
        "genderRank": 1,
        "categoryRank": 1
      }
    ]
  }
}
```

---

## ?? **Database Verification**

After each step, verify data:

```sql
-- After Step 5a: EPC Mappings
SELECT COUNT(*) as TotalAssignments
FROM ChipAssignments
WHERE EventId = {eventId} AND UnassignedAt IS NULL;

-- After Step 5b-c: Raw Readings
SELECT 
    DeviceId,
    COUNT(*) as TotalReadings,
    COUNT(DISTINCT Epc) as UniqueEPCs,
    ProcessResult,
    COUNT(*) as Count
FROM RawRFIDReading
GROUP BY DeviceId, ProcessResult;

-- After Step 6: Normalized Readings
SELECT 
    c.Name as CheckpointName,
    COUNT(DISTINCT rn.ParticipantId) as Participants,
    COUNT(*) as TotalReadings
FROM ReadNormalized rn
INNER JOIN Checkpoints c ON rn.CheckpointId = c.Id
WHERE rn.EventId = {eventId}
GROUP BY c.Name, c.DistanceFromStart
ORDER BY c.DistanceFromStart;

-- After Step 7a: Split Times
SELECT 
    c.Name as CheckpointName,
    c.DistanceFromStart,
    COUNT(DISTINCT st.ParticipantId) as Participants,
    COUNT(*) as TotalSplits,
    MIN(st.SplitTimeMs) as FastestMs,
    MAX(st.SplitTimeMs) as SlowestMs
FROM SplitTimes st
INNER JOIN Checkpoints c ON st.CheckpointId = c.Id
WHERE st.EventId = {eventId}
GROUP BY c.Name, c.DistanceFromStart
ORDER BY c.DistanceFromStart;

-- After Step 7b: Results
SELECT 
    Status,
    COUNT(*) as Count,
    MIN(FinishTime) as FastestMs,
    MAX(FinishTime) as SlowestMs
FROM Results
WHERE EventId = {eventId}
GROUP BY Status;

-- Top 10 Finishers
SELECT 
    OverallRank,
    p.BIBNumber,
    p.FirstName + ' ' + p.LastName as Name,
    r.FinishTime,
    r.GenderRank,
    r.CategoryRank
FROM Results r
INNER JOIN Participants p ON r.ParticipantId = p.Id
WHERE r.EventId = {eventId} AND r.Status = 'Finished'
ORDER BY r.OverallRank
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;
```

---

## ?? **API Endpoints Summary**

### **RFID Import**
- `POST /api/RFID/{eventId}/{raceId}/epc-mapping` - Upload EPC-BIB mapping
- `POST /api/RFID/{eventId}/{raceId}/import` - Upload RFID reading files
- `POST /api/RFID/{eventId}/{raceId}/import/{batchId}/process` - Process batch
- `POST /api/RFID/{eventId}/{raceId}/deduplicate` - Deduplicate readings

### **Results Calculation**
- `POST /api/Results/{eventId}/{raceId}/calculate-splits` - Calculate split times
- `POST /api/Results/{eventId}/{raceId}/calculate-results` - Calculate final results

### **Results Display**
- `GET /api/Results/{eventId}/{raceId}/leaderboard` - Get leaderboard
  - Query params: `rankBy`, `gender`, `category`, `page`, `pageSize`, `includeSplits`
- `GET /api/Results/{eventId}/{raceId}/participant/{participantId}` - Get participant result

---

## ?? **Complete Testing Script**

```bash
#!/bin/bash

API_URL="https://localhost:7001/api"
EVENT_ID="{your_event_id}"
RACE_ID="{your_race_id}"
TOKEN="{your_auth_token}"

echo "=== COMPLETE RFID WORKFLOW TEST ==="
echo ""

# Step 5a: Upload EPC mapping
echo "Step 5a: Uploading EPC-BIB mapping..."
EPC_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/epc-mapping" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@BHOPAL EPC DATA.xlsx")
echo "$EPC_RESPONSE" | jq '.'
echo ""

# Step 5b-c: Upload and process RFID files
declare -A FILES=(
  ["Box15"]="1. 2026-01-25_0016251292ae_(box15).db|{cpStartId}"
  ["Box16"]="1.1 2026-01-25_0016251292a1_(Box-16).db|{cp25Id}"
  ["Box19"]="2. 2026-01-25_00162512dbb0_(Box-19).db|{cp5Id}"
  ["Box24"]="2.1 2026-01-25_001625135f24_(box_24).db|{cp75Id}"
)

for DEVICE in "${!FILES[@]}"; do
  IFS='|' read -r FILE CHECKPOINT <<< "${FILES[$DEVICE]}"
  
  echo "Step 5b: Uploading $FILE..."
  UPLOAD_RESPONSE=$(curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/import" \
    -H "Authorization: Bearer $TOKEN" \
    -F "file=@$FILE" \
    -F "deviceId=$DEVICE" \
    -F "checkpointId=$CHECKPOINT" \
    -F "timeZoneId=Asia/Kolkata")
  
  BATCH_ID=$(echo "$UPLOAD_RESPONSE" | jq -r '.message.importBatchId')
  echo "Batch ID: $BATCH_ID"
  
  echo "Step 5c: Processing batch..."
  curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/import/$BATCH_ID/process" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"eventId\":\"$EVENT_ID\",\"raceId\":\"$RACE_ID\",\"importBatchId\":\"$BATCH_ID\"}" | jq '.'
  echo ""
done

# Step 6: Deduplicate
echo "Step 6: Deduplicating..."
curl -s -X POST "$API_URL/RFID/$EVENT_ID/$RACE_ID/deduplicate" \
  -H "Authorization: Bearer $TOKEN" | jq '.'
echo ""

# Step 7a: Calculate split times
echo "Step 7a: Calculating split times..."
curl -s -X POST "$API_URL/Results/$EVENT_ID/$RACE_ID/calculate-splits" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"eventId\":\"$EVENT_ID\",\"raceId\":\"$RACE_ID\"}" | jq '.'
echo ""

# Step 7b: Calculate results
echo "Step 7b: Calculating final results..."
curl -s -X POST "$API_URL/Results/$EVENT_ID/$RACE_ID/calculate-results" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"eventId\":\"$EVENT_ID\",\"raceId\":\"$RACE_ID\"}" | jq '.'
echo ""

# Step 8a: Get leaderboard
echo "Step 8a: Fetching leaderboard..."
curl -s -X GET "$API_URL/Results/$EVENT_ID/$RACE_ID/leaderboard?rankBy=overall&page=1&pageSize=10&includeSplits=true" \
  -H "Authorization: Bearer $TOKEN" | jq '.'
echo ""

echo "=== TESTING COMPLETE ==="
```

---

## ?? **Summary**

**What's Now Complete**:
- ? EPC-BIB mapping upload
- ? RFID file upload & processing
- ? Deduplication & normalization
- ? Split time calculation
- ? Results calculation
- ? Rankings (Overall, Gender, Category)
- ? Leaderboard API
- ? Individual participant results

**Data Flow**:
```
Excel (EPC-BIB) ? Chips + ChipAssignments
SQLite (.db) ? RawRFIDReading ? ReadNormalized
ReadNormalized ? SplitTimes ? Results
Results ? Leaderboard API
```

**You now have a complete, production-ready RFID race timing system!** ???????
