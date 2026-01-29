# RFID Data Reprocessing Guide

## Overview

This guide explains the various reprocessing options available in the RFID system and when to use each one.

---

## **Reprocessing Scenarios**

### 1. **Incremental Processing (Default)** ? Most Common

**Use When:**
- Adding new upload files
- Normal workflow after initial uploads
- No configuration changes

**What It Does:**
- Processes only NEW upload batches
- Skips already-processed data
- Fast and safe

**Endpoint:**
```http
POST /api/RFID/{eventId}/{raceId}/process-complete
```

**UI Action:** Click "Process New Data"

---

### 2. **Force Reprocess (After Config Changes)** ?? After Fixes

**Use When:**
- Changed checkpoint-to-device mappings
- Fixed device configurations
- Want to recalculate everything from scratch

**What It Does:**
1. Clears ALL processed data (Results, ReadNormalized, Assignments)
2. Keeps raw uploads intact
3. Reprocesses all batches with NEW configuration
4. Recalculates all results

**Endpoint:**
```http
POST /api/RFID/{eventId}/{raceId}/process-complete?forceReprocess=true
```

**UI Action:** Click "Reprocess All"

**Example:**
```bash
# Scenario: You realize Box-15 was mapped to wrong checkpoint
# 1. Fix the checkpoint mapping in database/UI
# 2. Call this endpoint to reprocess with correct mapping
curl -X POST "https://api.example.com/api/RFID/ABC123/XYZ789/process-complete?forceReprocess=true" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

---

### 3. **Participant-Level Reprocessing** ?? Manual Corrections

**Use When:**
- Changed participant's BIB number
- Reassigned RFID chip to different participant
- Fixed participant name/category
- Need to recalculate results for specific runners only

**What It Does:**
1. Clears processed data ONLY for specified participants
2. Keeps everyone else's data intact
3. Reprocesses those participants' readings
4. Recalculates their results and rankings

**Endpoint:**
```http
POST /api/RFID/{eventId}/{raceId}/participants/reprocess
Content-Type: application/json

["participantId1", "participantId2", ...]
```

**UI Action:** On Participant Edit screen, click "Save & Reprocess"

**Example:**
```bash
# Scenario: Changed BIB for participant #1001 from 1001 to 2001
# Reprocess just that participant
curl -X POST "https://api.example.com/api/RFID/ABC123/XYZ789/participants/reprocess" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '["PARTICIPANT_ID_ENCRYPTED"]'
```

---

### 4. **Batch-Level Reprocessing** ?? Fix Single File

**Use When:**
- One upload batch has wrong checkpoint assignment
- Specific device file needs reprocessing
- Don't want to reprocess ALL batches

**What It Does:**
1. Clears processed data ONLY for readings in this batch
2. Resets batch status to "uploaded"
3. Reprocesses just this batch
4. Leaves other batches untouched

**Endpoint:**
```http
POST /api/RFID/{eventId}/{raceId}/batches/{uploadBatchId}/reprocess
```

**UI Action:** On Batch Management screen, click batch-specific "Reprocess" button

**Example:**
```bash
# Scenario: box15.db was uploaded but device mapping was wrong for Box-15
# Fix the device mapping, then reprocess just that batch
curl -X POST "https://api.example.com/api/RFID/ABC123/XYZ789/batches/BATCH_ID/reprocess" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

---

### 5. **Complete Reset (Nuclear Option)** ?? Start Over

**Use When:**
- Everything is messed up beyond repair
- Want to delete ALL data and start fresh
- Testing/development environments

**What It Does:**
1. Deletes ALL processed data (Results, ReadNormalized, Assignments)
2. OPTIONALLY deletes raw uploads too
3. Race becomes completely empty (or keeps uploads for reprocessing)

**Endpoint:**
```http
DELETE /api/RFID/{eventId}/{raceId}/processed-data?keepUploads=true
```

**Parameters:**
- `keepUploads=true` (default): Deletes processed data, keeps uploads ? Can reprocess
- `keepUploads=false`: Deletes EVERYTHING ? Must re-upload files

**UI Action:** Click "Reset & Clear" (with confirmation dialog)

**Example:**
```bash
# Option 1: Clear processed data, keep uploads (can reprocess)
curl -X DELETE "https://api.example.com/api/RFID/ABC123/XYZ789/processed-data?keepUploads=true" \
  -H "Authorization: Bearer YOUR_TOKEN"

# Option 2: Delete EVERYTHING (must re-upload)
curl -X DELETE "https://api.example.com/api/RFID/ABC123/XYZ789/processed-data?keepUploads=false" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

---

## **Decision Flowchart**

```
Did you change checkpoint/device mappings?
?? YES ? Use Force Reprocess (#2)
?? NO
   ?? Did you edit ONE participant?
   ?  ?? YES ? Use Participant Reprocessing (#3)
   ?? NO
      ?? Did you fix ONE upload batch?
      ?  ?? YES ? Use Batch Reprocessing (#4)
      ?? NO
         ?? Is everything broken?
         ?  ?? YES ? Use Complete Reset (#5)
         ?? NO
            ?? Just adding new data ? Use Incremental Processing (#1)
```

---

## **Common Workflows**

### **Workflow 1: Initial Race Setup**

1. Upload all RFID files (box15.db, box16.db, box19.db, box24.db)
2. Click **"Process New Data"** (#1)
3. Verify results in Participants tab

### **Workflow 2: Fixed Wrong Checkpoint Mapping**

1. User notices Start times in Finish column
2. Realizes Box-15 was mapped to Finish instead of Start
3. **Fix checkpoint mapping** in database:
   ```sql
   UPDATE Checkpoint 
   SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
   WHERE Name = 'Start' AND RaceId = @RaceId;
   ```
4. Click **"Reprocess All"** (#2) with `forceReprocess=true`
5. Wait for complete reprocessing
6. Verify checkpoint times are now correct

### **Workflow 3: Corrected BIB Number**

1. User finds Participant #1001 has wrong BIB (should be 2001)
2. Edit participant: Change BIB from 1001 to 2001
3. Click **"Save & Reprocess"** (#3)
4. System reprocesses only that participant
5. Rankings automatically update

### **Workflow 4: Wrong File Uploaded**

1. User uploaded `box15.db` but it was actually from Box-19 device
2. Delete the wrong batch (or mark as inactive)
3. Upload correct file
4. Click **"Process New Data"** (#1)
5. System processes new batch only

### **Workflow 5: Complete Disaster Recovery**

1. Everything is wrong (wrong chips, wrong bibs, wrong checkpoints)
2. Click **"Reset & Clear"** (#5) with `keepUploads=false`
3. Re-upload all files
4. Reconfigure checkpoints correctly
5. Click **"Process New Data"** (#1)

---

## **API Response Examples**

### **Force Reprocess Success:**

```json
{
  "message": {
    "status": "Completed",
    "message": "Complete workflow finished: 250 finishers processed across 4 checkpoints",
    "totalProcessingTimeMs": 15420,
    "totalBatchesProcessed": 4,
    "successfulBatches": 4,
    "totalRawReadingsProcessed": 1200,
    "totalNormalizedReadings": 1000,
    "duplicatesRemoved": 200,
    "totalFinishers": 250,
    "warnings": [
      "Device 'Box-16' has multiple checkpoints (loop race detected)"
    ]
  }
}
```

### **Participant Reprocessing Success:**

```json
{
  "message": {
    "status": "Success",
    "message": "Successfully reprocessed 3 participants",
    "totalParticipantsRequested": 3,
    "participantsCleared": 3,
    "readingsCleared": 12,
    "resultsCleared": 3,
    "participantsReprocessed": 3,
    "readingsCreated": 12,
    "resultsCreated": 3,
    "processingTimeMs": 2340
  }
}
```

### **Clear Data Success:**

```json
{
  "message": {
    "status": "Success",
    "message": "Cleared processed data. Upload batches preserved and ready for reprocessing.",
    "resultsCleared": 250,
    "normalizedReadingsCleared": 1000,
    "assignmentsCleared": 1000,
    "readingsReset": 1200,
    "batchesReset": 4,
    "uploadsDeleted": 0,
    "summary": "Cleared 250 results, 1000 normalized readings, 1000 checkpoint assignments. Reset 4 batches."
  }
}
```

---

## **Safety Considerations**

### **Data Loss Prevention:**

1. **Backups:** Always backup database before major operations
2. **keepUploads:** Default is `true` to preserve raw data
3. **Incremental:** Default processing mode never deletes data
4. **Transaction Safety:** All operations use database transactions

### **Recommended Practices:**

? **DO:**
- Test in development environment first
- Backup database before clearing data
- Use incremental processing (#1) by default
- Use force reprocess (#2) after config changes
- Use participant reprocessing (#3) for corrections

? **DON'T:**
- Call force reprocess unnecessarily (wastes time)
- Use complete reset (#5) in production without backup
- Delete uploads (`keepUploads=false`) unless absolutely sure
- Reprocess during active race (wait for all uploads first)

---

## **Troubleshooting**

### **Issue: Reprocessing Takes Forever**

**Cause:** Too many participants or readings  
**Solution:** Use batch-level (#4) or participant-level (#3) reprocessing instead

### **Issue: Results Still Wrong After Reprocess**

**Cause:** Checkpoint mapping still incorrect  
**Solution:** 
1. Verify checkpoint mappings with:
   ```sql
   SELECT c.Name, c.DeviceId, d.DeviceId AS Serial
   FROM Checkpoint c
   LEFT JOIN Device d ON c.DeviceId = d.Id
   WHERE c.RaceId = @RaceId;
   ```
2. Fix mappings
3. Force reprocess again

### **Issue: "No pending batches" Error**

**Cause:** All batches already processed  
**Solution:** Use `forceReprocess=true` to clear and reprocess

### **Issue: Participant Not Found**

**Cause:** Encrypted participant ID is invalid or participant deleted  
**Solution:** Verify participant exists and use correct encrypted ID

---

## **Quick Reference**

| Scenario | Endpoint | Query Param | Keep Data? | Speed |
|----------|----------|-------------|------------|-------|
| New uploads | `/process-complete` | - | ? All | ? Fast |
| Config changed | `/process-complete` | `?forceReprocess=true` | ? Uploads | ?? Slow |
| Fix 1 participant | `/participants/reprocess` | - | ? Others | ? Fast |
| Fix 1 batch | `/batches/{id}/reprocess` | - | ? Others | ? Fast |
| Reset race | `DELETE /processed-data` | `?keepUploads=true` | ? Uploads | ? Instant |
| Nuclear reset | `DELETE /processed-data` | `?keepUploads=false` | ? None | ? Instant |

---

## **Support**

For issues not covered in this guide, check:
1. Application logs for detailed error messages
2. Device_Checkpoint_Mapping_Guide.md for configuration help
3. RFID_Data_Cleanup_Script.sql for manual database operations
