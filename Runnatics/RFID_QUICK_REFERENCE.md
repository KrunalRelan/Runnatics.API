# RFID Upload System - Quick Reference

## 🎯 System Overview

Upload and process RFID timing files (SQLite/CSV) to generate race results with automatic checkpoint assignment and deduplication.

---

## 📊 Database Tables

| Table | Purpose | Key Fields |
|-------|---------|------------|
| `UploadBatches` | Track file uploads | `RaceId`, `EventId`, `FileHash`, `Status`, `TotalReadings` |
| `RawRFIDReadings` | Individual tag reads | `BatchId`, `Epc`, `TimestampMs`, `ProcessResult` |
| `ReadingCheckpointAssignments` | Checkpoint mappings | `ReadingId`, `CheckpointId` |

---

## 🔗 API Endpoints

### 1. Upload EPC-BIB Mapping
```bash
curl -X POST "https://api.runnatics.com/api/rfid/{eventId}/{raceId}/epc-mapping" \
  -H "Authorization: Bearer {token}" \
  -F "File=@epc-mapping.xlsx"
```

**Response:**
```json
{
  "message": {
    "fileName": "epc-mapping.xlsx",
    "totalMappings": 500,
    "successCount": 498,
    "errorCount": 2,
    "status": "Completed"
  }
}
```

---

### 2. Upload RFID File
```bash
curl -X POST "https://api.runnatics.com/api/rfid/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "File=@rfid-readings.db" \
  -F "FileFormat=DB" \
  -F "TimeZoneId=Asia/Kolkata" \
  -F "ExpectedCheckpointId={checkpointId}"
```

**Response:**
```json
{
  "message": {
    "uploadBatchId": "encrypted-batch-id",
    "fileName": "rfid-readings.db",
    "uploadedAt": "2026-01-27T10:30:00Z",
    "totalReadings": 12450,
    "uniqueEpcs": 487,
    "timeRangeStart": 1706352000000,
    "timeRangeEnd": 1706366400000,
    "fileSizeBytes": 2048000,
    "fileFormat": "DB",
    "status": "uploaded"
  }
}
```

---

### 3. Process Upload Batch
```bash
curl -X POST "https://api.runnatics.com/api/rfid/{eventId}/{raceId}/import/{uploadBatchId}/process" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "uploadBatchId": "encrypted-batch-id",
    "eventId": "encrypted-event-id",
    "raceId": "encrypted-race-id",
    "deduplicationWindowSeconds": 3,
    "autoAssignCheckpoints": true,
    "minCheckpointGapSeconds": 60
  }'
```

**Response:**
```json
{
  "message": {
    "totalReadings": 12450,
    "duplicateCount": 342,
    "uniqueReadings": 12108,
    "autoAssignedCount": 11950,
    "manualReviewCount": 158,
    "checkpointBreakdown": [
      { "checkpointId": 1, "checkpointName": "Start", "readingCount": 487 },
      { "checkpointId": 2, "checkpointName": "5K", "readingCount": 485 },
      { "checkpointId": 3, "checkpointName": "10K Finish", "readingCount": 480 }
    ],
    "status": "completed"
  }
}
```

---

### 4. Deduplicate & Normalize
```bash
curl -X POST "https://api.runnatics.com/api/rfid/{eventId}/{raceId}/deduplicate" \
  -H "Authorization: Bearer {token}"
```

**Response:**
```json
{
  "message": {
    "totalReadings": 12108,
    "normalizedCount": 1461,
    "participantsMatched": 487,
    "splitTimesCalculated": 1452,
    "resultsGenerated": 487,
    "status": "Success"
  }
}
```

---

## 🔄 Processing Flow

```
1. Upload File
   ↓
   Status: "uploading" → "uploaded"
   
2. Process Batch
   ↓
   Status: "processing"
   ├─ Deduplicate readings
   ├─ Assign checkpoints
   └─ Status: "completed"
   
3. Normalize
   ↓
   ├─ Match EPCs to Participants
   ├─ Calculate split times
   └─ Generate results
```

---

## 📁 File Formats

### SQLite Database

**Expected Tables:**
- `TagReads` or `Reads`

**Required Columns:**
- `EPC` / `TagId` - RFID tag identifier
- `FirstSeenTime` / `ReadTime` - Timestamp
- `Antenna` / `AntennaPort` - Antenna number
- `PeakRSSI` / `RSSI` - Signal strength

**Example Query:**
```sql
SELECT 
    EPC,
    FirstSeenTime,
    Antenna,
    PeakRSSI,
    ChannelIndex,
    TagSeenCount
FROM TagReads
ORDER BY FirstSeenTime;
```

---

### CSV Format

**Required Columns:**
```
EPC,Timestamp,Antenna,RSSI,Channel
E200001234567890,1706352000000,1,-45.2,0
E200001234567891,1706352003000,1,-48.5,0
```

**Column Descriptions:**
- `EPC` - RFID tag EPC code
- `Timestamp` - Unix timestamp in milliseconds
- `Antenna` - Antenna port (1-4)
- `RSSI` - Signal strength in dBm
- `Channel` - Channel index (optional)

---

## 🔧 Configuration

### Deduplication Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `DeduplicationWindowSeconds` | 3 | Remove duplicate reads within this window |

**Example:** If the same tag is read at 10:00:00 and 10:00:02, the second read is marked as duplicate.

---

### Checkpoint Assignment Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `AutoAssignCheckpoints` | true | Automatically assign readings to checkpoints |
| `MinCheckpointGapSeconds` | 60 | Minimum time between checkpoints |

**Logic:**
```
Tag reads at: 08:00:00, 08:45:00, 09:30:00
Gaps: 45min, 45min
If gap > 60sec → new checkpoint
Result: Checkpoint 1, Checkpoint 2, Checkpoint 3
```

---

## 🎯 Status Values

### UploadBatch.Status
- `uploading` - File is being uploaded
- `uploaded` - File upload complete
- `processing` - Processing readings
- `completed` - Processing finished
- `failed` - Error occurred

### RawRFIDReading.ProcessResult
- `Pending` - Not yet processed
- `Success` - Valid unique reading
- `Duplicate` - Duplicate within deduplication window
- `Invalid` - Failed validation

### RawRFIDReading.AssignmentMethod
- `Auto` - Automatically assigned by time-gap algorithm
- `Manual` - Manually assigned by admin
- `Sequential` - Sequentially assigned to checkpoints
- `TimeGap` - Time-gap based assignment

---

## 🔍 Common Queries

### Get Upload Batches for Race
```sql
SELECT 
    Id,
    OriginalFileName,
    TotalReadings,
    UniqueEpcs,
    Status,
    CreatedAt
FROM UploadBatches
WHERE RaceId = @raceId
  AND IsDeleted = 0
ORDER BY CreatedAt DESC;
```

### Get Readings for Batch
```sql
SELECT 
    Id,
    Epc,
    ReadTimeUtc,
    Antenna,
    RssiDbm,
    ProcessResult
FROM RawRFIDReadings
WHERE BatchId = @batchId
  AND IsDeleted = 0
ORDER BY TimestampMs;
```

### Get Checkpoint Assignments
```sql
SELECT 
    r.Epc,
    r.ReadTimeUtc,
    c.Name AS CheckpointName,
    c.DistanceFromStart,
    rca.CreatedAt AS AssignedAt
FROM RawRFIDReadings r
INNER JOIN ReadingCheckpointAssignments rca ON r.Id = rca.ReadingId
INNER JOIN Checkpoints c ON rca.CheckpointId = c.Id
WHERE r.BatchId = @batchId
ORDER BY r.TimestampMs;
```

### Find Duplicates
```sql
SELECT 
    Epc,
    COUNT(*) AS ReadCount,
    MIN(ReadTimeUtc) AS FirstRead,
    MAX(ReadTimeUtc) AS LastRead
FROM RawRFIDReadings
WHERE BatchId = @batchId
  AND ProcessResult = 'Duplicate'
GROUP BY Epc
ORDER BY ReadCount DESC;
```

---

## ⚠️ Troubleshooting

### Problem: File upload fails
**Check:**
- File size < 100 MB
- File format is SQLite (.db) or CSV (.csv)
- File is not corrupted

### Problem: No readings found
**Check:**
- SQLite file has `TagReads` or `Reads` table
- CSV file has correct column headers
- File is not empty

### Problem: Duplicate count too high
**Solution:**
- Increase `DeduplicationWindowSeconds` (e.g., 5 seconds)
- Check if RFID reader has multiple antennas reading same area

### Problem: Checkpoint assignment incorrect
**Solution:**
- Adjust `MinCheckpointGapSeconds`
- Use manual review for ambiguous readings
- Verify checkpoint order and distances

### Problem: Participants not matched
**Check:**
- EPC-BIB mapping uploaded for all participants
- ChipAssignments exist and are active
- EPC values match exactly (case-sensitive)

---

## 📚 Entity Relationships

```
UploadBatch
  └─ RawRFIDReading (1:N)
      └─ ReadingCheckpointAssignment (1:N)
          └─ Checkpoint

Chip
  └─ ChipAssignment (1:N)
      └─ Participant

RawRFIDReading (via EPC → Chip → ChipAssignment → Participant)
  └─ ReadNormalized
      └─ SplitTime
          └─ Result
```

---

## 🔐 Security

- All IDs in API responses are encrypted
- File uploads require `SuperAdmin` or `Admin` role
- Tenant-based access control enforced
- File hash prevents duplicate uploads
- SQL injection prevention in SQLite parsing

---

## 📊 Performance Tips

1. **Batch processing** - Process large uploads in background jobs
2. **Index usage** - Queries automatically use indexes on `Epc`, `TimestampMs`, `ProcessResult`
3. **Pagination** - Use pagination for large result sets
4. **Caching** - Cache checkpoint and participant data
5. **Archiving** - Archive completed batches after 90 days

---

## 📖 Related Documentation

- [RFID_IMPLEMENTATION_GUIDE.md](./RFID_IMPLEMENTATION_GUIDE.md) - Detailed implementation guide
- [REFACTORING_SUMMARY.md](./REFACTORING_SUMMARY.md) - Refactoring summary
- Database schema: `CreateRFIDTables_Fixed.sql`

---

*Quick Reference v1.0*  
*Last Updated: 2026-01-27*
