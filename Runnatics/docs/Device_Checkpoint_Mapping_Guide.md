# Device-to-Checkpoint Mapping Configuration Guide

## Overview

The RFID system uses **device-based checkpoint assignment** to ensure readings from each physical RFID reader (device) are automatically assigned to the correct checkpoint. This eliminates manual checkpoint selection errors and ensures accurate timing data.

## How It Works

### 1. **Device Identification**
Each RFID reader device (e.g., "Box-15", "Box-16") has a unique serial number embedded in the filename:
- `2026-01-25_0016251292ae_(box15).db` ? Device Serial: `0016251292ae`
- `2026-01-25_0016251292a1_(Box-16).db` ? Device Serial: `0016251292a1`
- `2026-01-25_00162512dbb0_(Box-19).db` ? Device Serial: `00162512dbb0`
- `2026-01-25_001625135f24_(box_24).db` ? Device Serial: `001625135f24`

### 2. **Device Registration**
Devices must be registered in the `Device` table with their serial number stored in the `DeviceId` column:

```sql
INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
VALUES 
    (1, 'Box-15 (Start/Finish)', '0016251292ae', 1, GETUTCDATE(), 1, 0),
    (1, 'Box-16 (Backup)', '0016251292a1', 1, GETUTCDATE(), 1, 0),
    (1, 'Box-19 (5KM)', '00162512dbb0', 1, GETUTCDATE(), 1, 0),
    (1, 'Box-24 (Backup 5KM)', '001625135f24', 1, GETUTCDATE(), 1, 0);
```

### 3. **Checkpoint Mapping**

#### **Simple Race** (Each device = One checkpoint)
Each checkpoint is mapped to a unique device:

```sql
UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
WHERE Name = 'Start' AND RaceId = @RaceId;

UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '00162512dbb0')
WHERE Name = '5KM' AND RaceId = @RaceId;

UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '001625135f24')
WHERE Name = 'Finish' AND RaceId = @RaceId;
```

#### **Loop/Lap Race** (One device = Multiple checkpoints) ? NEW
For races where Start and Finish share the same location (same device), map BOTH checkpoints to the SAME device:

```sql
-- Both Start and Finish use Box-15 (loop course)
UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
WHERE Name = 'Start' AND RaceId = @RaceId;

UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
WHERE Name = 'Finish' AND RaceId = @RaceId;

UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '00162512dbb0')
WHERE Name = '5KM' AND RaceId = @RaceId;
```

**The system automatically detects loop races and assigns readings based on timing sequence:**
- **1st reading** from Box-15 ? **Start** (0 KM at 08:00:00)
- **2nd reading** from Box-15 ? **Finish** (10 KM at 09:30:00)

## Database Schema

### Device Table
```sql
CREATE TABLE Device (
    Id INT PRIMARY KEY IDENTITY(1,1),
    TenantId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    DeviceId NVARCHAR(100) NOT NULL,  -- Serial number from filename
    CreatedBy INT NOT NULL,
    CreatedDate DATETIME2(7) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    IsDeleted BIT NOT NULL DEFAULT 0
);
```

### Checkpoint Table
```sql
CREATE TABLE Checkpoint (
    Id INT PRIMARY KEY IDENTITY(1,1),
    EventId INT NOT NULL,
    RaceId INT NOT NULL,
    Name NVARCHAR(100),
    DistanceFromStart DECIMAL(18,2) NOT NULL,
    DeviceId INT,  -- Foreign key to Device.Id
    ParentDeviceId INT NULL,  -- For backup/redundant readers
    IsMandatory BIT NOT NULL,
    -- ... audit fields
);
```

## Configuration Steps

### Important: Loop Race Detection

**The system automatically detects loop/lap races when multiple checkpoints are mapped to the same device.**

**Example Scenario:**
- **10 KM Loop Race**: Start at 0 KM, run 5 KM, return to Start (now Finish at 10 KM)
- **Device Setup**: Box-15 placed at Start/Finish line
- **Configuration**:
  ```sql
  -- Map BOTH checkpoints to Box-15
  UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
  WHERE Name = 'Start' AND DistanceFromStart = 0;
  
  UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
  WHERE Name = 'Finish' AND DistanceFromStart = 10;
  ```

**How Readings Are Assigned:**
1. System detects Box-15 has 2 checkpoints (Start at 0 KM, Finish at 10 KM)
2. Groups readings by participant
3. Sorts each participant's readings by timestamp
4. **1st reading** ? Assigned to **Start** (0 KM)
5. **2nd reading** ? Assigned to **Finish** (10 KM)

**?? Important:** Checkpoints MUST be ordered by `DistanceFromStart` for correct assignment!

### Step 1: Verify Devices Exist

Run this query to check registered devices:

```sql
SELECT Id, Name, DeviceId AS Serial, TenantId
FROM Device
WHERE IsActive = 1 AND IsDeleted = 0
ORDER BY Name;
```

**Expected Result:**
| Id | Name | Serial | TenantId |
|----|------|--------|----------|
| 1  | Box-15 (Start) | 0016251292ae | 1 |
| 2  | Box-16 (5KM) | 0016251292a1 | 1 |
| 3  | Box-19 (10KM) | 00162512dbb0 | 1 |
| 4  | Box-24 (Finish) | 001625135f24 | 1 |

### Step 2: Verify Checkpoint Mappings

```sql
SELECT 
    c.Id,
    c.Name,
    c.DistanceFromStart,
    c.DeviceId,
    d.DeviceId AS DeviceSerial,
    d.Name AS DeviceName
FROM Checkpoint c
LEFT JOIN Device d ON c.DeviceId = d.Id
WHERE c.RaceId = @RaceId 
  AND c.IsActive = 1 
  AND c.IsDeleted = 0
ORDER BY c.DistanceFromStart;
```

**Expected Result:**
| Id | Name | Distance | DeviceId | DeviceSerial | DeviceName |
|----|------|----------|----------|--------------|------------|
| 1  | Start | 0.00 | 1 | 0016251292ae | Box-15 (Start) |
| 2  | 5KM | 5.00 | 2 | 0016251292a1 | Box-16 (5KM) |
| 3  | 10KM | 10.00 | 3 | 00162512dbb0 | Box-19 (10KM) |
| 4  | Finish | 21.10 | 4 | 001625135f24 | Box-24 (Finish) |

### Step 3: Upload RFID Files

When uploading SQLite files through the API:

1. **Filename must contain device serial**: `2026-01-25_0016251292ae_(box15).db`
2. **System automatically extracts serial**: `0016251292ae`
3. **Looks up Device record**: `SELECT Id FROM Device WHERE DeviceId = '0016251292ae'`
4. **Finds mapped Checkpoint**: `SELECT Id FROM Checkpoint WHERE DeviceId = @DeviceId AND RaceId = @RaceId`
5. **Assigns all readings to that checkpoint**: No manual selection needed!

### Step 4: Process Data

1. Click **"Process Result"** button in UI
2. System runs 3-phase workflow:
   - **Phase 1**: Process raw readings ? Assign to checkpoints based on device mapping
   - **Phase 2**: Deduplicate readings ? Create `ReadNormalized` records
   - **Phase 3**: Calculate results ? Populate `Results` table with rankings

## Troubleshooting

### Issue 1: No Checkpoint Times Showing

**Symptoms:**
- Participants tab shows empty checkpoint columns
- Results table has "Finished" status but no checkpoint data

**Solution:**
1. Check device-checkpoint mappings:
   ```sql
   SELECT c.Name, c.DeviceId, d.DeviceId AS Serial
   FROM Checkpoint c
   LEFT JOIN Device d ON c.DeviceId = d.Id
   WHERE c.RaceId = @RaceId;
   ```

2. If `DeviceId` is NULL or 0, update mappings (see Step 2 above)

3. Clear bad data and reprocess:
   ```sql
   -- Use RFID_Data_Cleanup_Script.sql
   DELETE FROM ReadNormalized WHERE EventId = @EventId;
   DELETE FROM ReadingCheckpointAssignment WHERE ...;
   -- Then click "Process Result" again
   ```

### Issue 2: Loop Race - Start Times in Finish Column

**Symptoms:**
- Finish column shows times from Start line (e.g., 08:00:00 instead of 09:30:00)
- Start column empty or has Finish times

**Root Cause:** Checkpoints not ordered correctly by `DistanceFromStart`

**Solution:**
1. Verify checkpoint distances:
   ```sql
   SELECT Name, DistanceFromStart, DeviceId
   FROM Checkpoint
   WHERE RaceId = @RaceId
   ORDER BY DistanceFromStart;
   ```

2. Ensure Start has lower distance than Finish:
   ```sql
   -- Start should be 0 KM
   UPDATE Checkpoint SET DistanceFromStart = 0
   WHERE Name = 'Start' AND RaceId = @RaceId;
   
   -- Finish should be race distance (e.g., 10 KM)
   UPDATE Checkpoint SET DistanceFromStart = 10
   WHERE Name = 'Finish' AND RaceId = @RaceId;
   ```

3. Clear data and reprocess (see Issue 1 cleanup)

### Issue 3: Loop Race - Too Many Readings

**Symptoms:**
- Some participants have 3+ readings from loop device
- Warnings in logs: "Participant has extra reading #3 beyond 2 checkpoints"

**Root Cause:** Participant passed the device more than expected (false reads or they ran extra loops)

**Solution:**
1. Check application logs for warnings about extra readings
2. Verify actual participant timing:
   ```sql
   SELECT p.BibNumber, r.ReadTimeUtc, r.TimestampMs
   FROM RawRFIDReading r
   INNER JOIN ChipAssignment ca ON r.Epc = ca.Chip.EPC
   INNER JOIN Participant p ON ca.ParticipantId = p.Id
   WHERE r.BatchId = @BatchId
     AND p.Id = @ParticipantId
   ORDER BY r.TimestampMs;
   ```
3. Extra readings are automatically ignored (only first N readings used, where N = number of checkpoints)

### Issue 4: Wrong Times in Checkpoint Columns

**Symptoms:**
- Start column shows 5KM times
- Times from different checkpoints are mixed

**Root Cause:** Checkpoint mappings are incorrect (e.g., Box-15 mapped to 5KM instead of Start)

**Solution:**
1. Verify actual device locations:
   - Which box is physically at Start line?
   - Which box is at 5KM mark?

2. Update mappings to match physical reality:
   ```sql
   UPDATE Checkpoint SET DeviceId = (SELECT Id FROM Device WHERE DeviceId = '0016251292ae')
   WHERE Name = 'Start';
   ```

3. Reprocess data (see Issue 1)

### Issue 3: Parent/Child Checkpoint Times Too Far Apart

**Symptoms:**
- 5KM Parent shows 01:27:35
- 5KM Child shows 01:02:33 (25+ minutes difference!)

**Root Cause:** Child device is actually at a different checkpoint

**Solution:**
1. Check which devices are configured as parent/child:
   ```sql
   SELECT Name, DeviceId, ParentDeviceId
   FROM Checkpoint
   WHERE RaceId = @RaceId;
   ```

2. Verify physical device placement - parent/child should be at SAME location (backup)

3. If devices are at different locations, they should be separate checkpoints, not parent/child

### Issue 4: Device Not Found Error

**Error:** `Device '{serial}' not found in the system`

**Solution:**
1. Check if device exists:
   ```sql
   SELECT * FROM Device WHERE DeviceId = '0016251292ae';
   ```

2. If not found, create it:
   ```sql
   INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
   VALUES (1, 'Box-15', '0016251292ae', 1, GETUTCDATE(), 1, 0);
   ```

## Best Practices

### 1. Device Naming Convention
- Use descriptive names: `Box-15 (Start/Finish)` instead of just `Box-15`
- Include location in name for easy identification
- For loop races, indicate multiple checkpoints: `Box-15 (Start/Finish)`

### 2. Filename Consistency
- Always include device serial in filename: `{date}_{serial}_{description}.db`
- Example: `2026-01-25_0016251292ae_(Start-Finish).db`

### 3. Checkpoint Distance Configuration
- **CRITICAL**: Always set correct `DistanceFromStart` values
- Start checkpoint: `DistanceFromStart = 0`
- Intermediate checkpoints: Actual distance in KM
- Finish checkpoint: Total race distance
- **For loop races**: Finish distance = Start distance + loop distance

### 4. Loop/Lap Race Setup
- **Map multiple checkpoints to same device** when they're at the same physical location
- Example 10 KM loop race:
  ```sql
  -- Start: 0 KM (Box-15)
  INSERT INTO Checkpoint (Name, DistanceFromStart, DeviceId, RaceId, EventId)
  VALUES ('Start', 0, (SELECT Id FROM Device WHERE DeviceId = '0016251292ae'), @RaceId, @EventId);
  
  -- 5 KM: Halfway point (Box-19)
  INSERT INTO Checkpoint (Name, DistanceFromStart, DeviceId, RaceId, EventId)
  VALUES ('5 KM', 5, (SELECT Id FROM Device WHERE DeviceId = '00162512dbb0'), @RaceId, @EventId);
  
  -- Finish: 10 KM (Box-15 - SAME as Start!)
  INSERT INTO Checkpoint (Name, DistanceFromStart, DeviceId, RaceId, EventId)
  VALUES ('Finish', 10, (SELECT Id FROM Device WHERE DeviceId = '0016251292ae'), @RaceId, @EventId);
  ```

### 5. Parent/Child Checkpoints
- Only use ParentDeviceId for backup readers at THE SAME physical location
- If devices are at different distances, create separate checkpoints

### 4. Testing
1. Upload one file first
2. Click "Process Result"
3. Check Participants tab for checkpoint times
4. Verify times make sense (Start < 5KM < 10KM < Finish)
5. If times are wrong, check mappings and reprocess

### 5. Data Cleanup
- Always backup data before cleanup: `SELECT * INTO Table_Backup FROM Table`
- Use provided `RFID_Data_Cleanup_Script.sql` for safe cleanup
- Test with one race before applying to all races

## API Workflow

### Automatic Upload (Recommended)
```http
POST /api/RFID/import-auto
Content-Type: multipart/form-data

File: 2026-01-25_0016251292ae_(box15).db
```

System automatically:
1. Extracts `0016251292ae` from filename
2. Finds device with `DeviceId = '0016251292ae'`
3. Finds checkpoint with `DeviceId = {device.Id}`
4. Assigns all readings to that checkpoint

### Manual Upload (Legacy)
```http
POST /api/RFID/{eventId}/{raceId}/import
Content-Type: multipart/form-data

File: readings.db
DeviceId: box15
ExpectedCheckpointId: (ignored - auto-detected from device mapping)
```

## Validation Queries

### Check Upload Batches
```sql
SELECT 
    ub.OriginalFileName,
    ub.DeviceId AS DeviceSerial,
    c.Name AS AssignedCheckpoint,
    ub.TotalReadings,
    ub.Status
FROM UploadBatch ub
LEFT JOIN Checkpoint c ON ub.ExpectedCheckpointId = c.Id
WHERE ub.RaceId = @RaceId
ORDER BY ub.CreatedDate;
```

### Check Reading Distribution
```sql
SELECT 
    c.Name AS Checkpoint,
    COUNT(DISTINCT rn.ParticipantId) AS Participants,
    COUNT(rn.Id) AS TotalReadings,
    MIN(rn.ChipTime) AS EarliestTime,
    MAX(rn.ChipTime) AS LatestTime
FROM ReadNormalized rn
INNER JOIN Checkpoint c ON rn.CheckpointId = c.Id
WHERE rn.EventId = @EventId
GROUP BY c.Name, c.DistanceFromStart
ORDER BY c.DistanceFromStart;
```

### Sample Participant Times
```sql
SELECT TOP 10
    p.BibNumber,
    p.FullName,
    c.Name AS Checkpoint,
    rn.ChipTime,
    rn.GunTime
FROM ReadNormalized rn
INNER JOIN Participant p ON rn.ParticipantId = p.Id
INNER JOIN Checkpoint c ON rn.CheckpointId = c.Id
WHERE rn.EventId = @EventId
ORDER BY p.BibNumber, c.DistanceFromStart;
```

## Support

If you encounter issues not covered in this guide:

1. Run `RFID_Data_Cleanup_Script.sql` SECTION 1-3 to view current state
2. Check application logs for device mapping warnings:
   - `Device '{serial}' found but no checkpoint assigned`
   - `No checkpoint is assigned to device '{name}'`
3. Verify database schema matches expected structure
4. Contact support with:
   - Race ID
   - Uploaded filenames
   - Current checkpoint mappings (from validation query)
