# RFID Import Implementation - Complete Guide

## ?? Package Installed
? **System.Data.SQLite.Core** v1.0.119 - Added to Runnatics.Services project

## ?? Implementation Summary

### Files Created (17 total)

#### Entity Models (3)
1. `Runnatics\src\Runnatics.Models.Data\Entities\RawRFIDReading.cs`
2. `Runnatics\src\Runnatics.Models.Data\Entities\UploadBatch.cs`
3. `Runnatics\src\Runnatics.Models.Data\Entities\ReadingCheckpointAssignment.cs`

#### EF Core Configurations (3)
4. `Runnatics\src\Runnatics.Data.EF\Config\RawRFIDReadingConfiguration.cs`
5. `Runnatics\src\Runnatics.Data.EF\Config\UploadBatchConfiguration.cs`
6. `Runnatics\src\Runnatics.Data.EF\Config\ReadingCheckpointAssignmentConfiguration.cs`

#### Request DTOs (2)
7. `Runnatics\src\Runnatics.Models.Client\Requests\RFID\RFIDImportRequest.cs`
8. `Runnatics\src\Runnatics.Models.Client\Requests\RFID\ProcessRFIDImportRequest.cs`

#### Response DTOs (2)
9. `Runnatics\src\Runnatics.Models.Client\Responses\RFID\RFIDImportResponse.cs`
10. `Runnatics\src\Runnatics.Models.Client\Responses\RFID\ProcessRFIDImportResponse.cs`

#### Service Layer (2)
11. `Runnatics\src\Runnatics.Services.Interface\IRFIDImportService.cs`
12. `Runnatics\src\Runnatics.Services\RFIDImportService.cs`

#### API Controller (1)
13. `Runnatics\src\Runnatics.Api\Controller\RFIDController.cs`

#### Updated Files (2)
14. `Runnatics\src\Runnatics.Data.EF\RaceSyncDbContext.cs` - Added RFID DbSets
15. `Runnatics\src\Runnatics.Api\Program.cs` - Registered IRFIDImportService

---

## ??? Database Migration

### Option 1: Using EF Core Migrations (Recommended)

```bash
# Navigate to project root
cd C:\repositories\Runnatics.API\Runnatics\src\Runnatics.Data.EF

# Create migration
dotnet ef migrations add AddRFIDImportTables --startup-project ..\Runnatics.Api

# Apply migration
dotnet ef database update --startup-project ..\Runnatics.Api
```

### Option 2: Manual SQL Scripts

```sql
-- Create UploadBatches Table
CREATE TABLE [dbo].[UploadBatches] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [RaceId] INT NOT NULL,
    [EventId] INT NOT NULL,
    [DeviceId] NVARCHAR(50) NOT NULL,
    [ReaderDeviceId] INT NULL,
    [ExpectedCheckpointId] INT NULL,
    [OriginalFileName] NVARCHAR(255) NOT NULL,
    [StoredFilePath] NVARCHAR(500) NULL,
    [FileSizeBytes] BIGINT NOT NULL,
    [FileHash] NVARCHAR(50) NULL,
    [FileFormat] NVARCHAR(20) NOT NULL DEFAULT 'DB',
    [Status] NVARCHAR(20) NOT NULL DEFAULT 'uploading',
    [TotalReadings] INT NULL,
    [UniqueEpcs] INT NULL,
    [TimeRangeStart] BIGINT NULL,
    [TimeRangeEnd] BIGINT NULL,
    [SourceType] NVARCHAR(20) NOT NULL DEFAULT 'file_upload',
    [IsLiveSync] BIT NOT NULL DEFAULT 0,
    [ProcessingStartedAt] DATETIME2 NULL,
    [ProcessingCompletedAt] DATETIME2 NULL,
    [CreatedBy] INT NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedBy] INT NULL,
    [UpdatedAt] DATETIME2 NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [FK_UploadBatches_Races] FOREIGN KEY ([RaceId]) 
        REFERENCES [dbo].[Races]([Id]),
    CONSTRAINT [FK_UploadBatches_Events] FOREIGN KEY ([EventId]) 
        REFERENCES [dbo].[Events]([Id]),
    CONSTRAINT [FK_UploadBatches_Devices] FOREIGN KEY ([ReaderDeviceId]) 
        REFERENCES [dbo].[Devices]([Id]),
    CONSTRAINT [FK_UploadBatches_Checkpoints] FOREIGN KEY ([ExpectedCheckpointId]) 
        REFERENCES [dbo].[Checkpoints]([Id])
);

CREATE INDEX IX_UploadBatches_RaceId ON [dbo].[UploadBatches]([RaceId]);
CREATE INDEX IX_UploadBatches_EventId ON [dbo].[UploadBatches]([EventId]);
CREATE INDEX IX_UploadBatches_FileHash ON [dbo].[UploadBatches]([FileHash]);
CREATE INDEX IX_UploadBatches_Status ON [dbo].[UploadBatches]([Status]);

-- Create RawRFIDReadings Table
CREATE TABLE [dbo].[RawRFIDReadings] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [BatchId] INT NOT NULL,
    [DeviceId] NVARCHAR(50) NOT NULL,
    [Epc] NVARCHAR(50) NOT NULL,
    [TimestampMs] BIGINT NOT NULL,
    [Antenna] INT NULL,
    [RssiDbm] DECIMAL(5,2) NULL,
    [Channel] INT NULL,
    [ReadTimeLocal] DATETIME2 NOT NULL,
    [ReadTimeUtc] DATETIME2 NOT NULL,
    [TimeZoneId] NVARCHAR(50) NOT NULL DEFAULT 'UTC',
    [ProcessResult] NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    [AssignmentMethod] NVARCHAR(20) NULL,
    [CheckpointConfidence] DECIMAL(5,4) NULL,
    [RequiresManualReview] BIT NOT NULL DEFAULT 0,
    [IsManualEntry] BIT NOT NULL DEFAULT 0,
    [ManualTimeOverride] DATETIME2 NULL,
    [DuplicateOfReadingId] BIGINT NULL,
    [ProcessedAt] DATETIME2 NULL,
    [SourceType] NVARCHAR(20) NOT NULL DEFAULT 'file_upload',
    [Notes] NVARCHAR(MAX) NULL,
    [CreatedBy] INT NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedBy] INT NULL,
    [UpdatedAt] DATETIME2 NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [FK_RawRFIDReadings_UploadBatches] FOREIGN KEY ([BatchId]) 
        REFERENCES [dbo].[UploadBatches]([Id]) ON DELETE CASCADE
);

CREATE INDEX IX_RawRFIDReadings_BatchId ON [dbo].[RawRFIDReadings]([BatchId]);
CREATE INDEX IX_RawRFIDReadings_Epc ON [dbo].[RawRFIDReadings]([Epc]);
CREATE INDEX IX_RawRFIDReadings_ProcessResult ON [dbo].[RawRFIDReadings]([ProcessResult]);
CREATE INDEX IX_RawRFIDReadings_Epc_TimestampMs ON [dbo].[RawRFIDReadings]([Epc], [TimestampMs]);

-- Create ReadingCheckpointAssignments Table
CREATE TABLE [dbo].[ReadingCheckpointAssignments] (
    [Id] INT IDENTITY(1,1) PRIMARY KEY,
    [ReadingId] BIGINT NOT NULL,
    [CheckpointId] INT NOT NULL,
    [DetectionId] INT NULL,
    [CreatedBy] INT NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedBy] INT NULL,
    [UpdatedAt] DATETIME2 NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [FK_ReadingCheckpointAssignments_RawRFIDReadings] FOREIGN KEY ([ReadingId]) 
        REFERENCES [dbo].[RawRFIDReadings]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ReadingCheckpointAssignments_Checkpoints] FOREIGN KEY ([CheckpointId]) 
        REFERENCES [dbo].[Checkpoints]([Id])
);

CREATE INDEX IX_ReadingCheckpointAssignments_ReadingId ON [dbo].[ReadingCheckpointAssignments]([ReadingId]);
CREATE INDEX IX_ReadingCheckpointAssignments_CheckpointId ON [dbo].[ReadingCheckpointAssignments]([CheckpointId]);
CREATE UNIQUE INDEX IX_ReadingCheckpointAssignments_ReadingId_CheckpointId 
    ON [dbo].[ReadingCheckpointAssignments]([ReadingId], [CheckpointId]);
```

---

## ?? API Endpoints

### 1. Upload RFID File
**Endpoint**: `POST /api/rfid/{eventId}/{raceId}/import`

**Authorization**: SuperAdmin, Admin

**Request** (multipart/form-data):
```
File: [SQLite database file]
DeviceId: "string" (optional)
CheckpointId: "encrypted-id" (optional)
TimeZoneId: "America/New_York" (default: "UTC")
TreatAsUtc: false (default: false)
```

**Response** (200 OK):
```json
{
  "message": {
    "importBatchId": "encrypted-batch-id",
    "fileName": "rfid_readings.db",
    "uploadedAt": "2024-01-22T10:30:00Z",
    "totalRecords": 1523,
    "validRecords": 1523,
    "invalidRecords": 0,
    "status": "Uploaded",
    "errors": []
  }
}
```

**Curl Example**:
```bash
curl -X POST "https://api.runnatics.com/api/rfid/{eventId}/{raceId}/import" \
  -H "Authorization: Bearer {token}" \
  -F "File=@rfid_readings.db" \
  -F "DeviceId=Reader-001" \
  -F "CheckpointId=encrypted-checkpoint-id" \
  -F "TimeZoneId=America/New_York" \
  -F "TreatAsUtc=false"
```

---

### 2. Process RFID Readings
**Endpoint**: `POST /api/rfid/{eventId}/{raceId}/import/{importBatchId}/process`

**Authorization**: SuperAdmin, Admin

**Request Body**:
```json
{
  "importBatchId": "encrypted-batch-id",
  "eventId": "encrypted-event-id",
  "raceId": "encrypted-race-id"
}
```

**Response** (200 OK):
```json
{
  "message": {
    "importBatchId": 123,
    "processedAt": "2024-01-22T10:35:00Z",
    "successCount": 1450,
    "errorCount": 73,
    "unlinkedCount": 15,
    "status": "CompletedWithErrors",
    "unlinkedEPCs": [
      "E2003412011234567890ABCD",
      "E2003412011234567890ABCE"
    ],
    "errors": []
  }
}
```

---

## ?? Workflow

### Typical RFID Import Process:

1. **Participant Setup**:
   - Ensure participants have RFID tags assigned (`RFIDTag` field populated)
   - This links the RFID EPC code to a specific participant

2. **Upload RFID File**:
   - Export SQLite database from RFID reader device
   - Upload via `POST /api/rfid/{eventId}/{raceId}/import`
   - System parses and stores raw readings

3. **Process Readings**:
   - Call `POST /api/rfid/{eventId}/{raceId}/import/{importBatchId}/process`
   - System links RFID tags (EPCs) to participants
   - Validates signal strength (RSSI)
   - Creates checkpoint assignments if checkpoint specified

4. **Review Results**:
   - Check `successCount` - Successfully linked readings
   - Check `unlinkedEPCs` - Tags without matching participants
   - Review weak signals (RSSI < -75 dBm)

---

## ?? Key Features

### Duplicate Prevention
- MD5 file hashing prevents uploading same file twice
- Checks for duplicate uploads per race

### Timezone Support
- **TreatAsUtc = false** (default): Timestamp is local time, converts to UTC
- **TreatAsUtc = true**: Timestamp is already UTC
- Supports any .NET timezone ID (e.g., "America/New_York", "Europe/London")

### Signal Quality Validation
- Filters readings with RSSI < -75 dBm (weak signal)
- Marks as "Invalid" with reason in notes

### Data Tracking
- Full audit trail (CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
- Soft delete support (IsActive, IsDeleted)
- Processing status tracking (Pending, Success, Duplicate, Invalid)

### SQLite Schema Expected
The RFID reader SQLite file should have this structure:
```sql
CREATE TABLE tags (
    id INTEGER PRIMARY KEY,
    epc TEXT NOT NULL,
    time INTEGER NOT NULL,    -- Unix timestamp in milliseconds
    antenna INTEGER,
    rssi REAL,
    channel INTEGER
);
```

---

## ?? Data Flow

```
1. Upload SQLite File
   ?
2. Create UploadBatch record
   ?
3. Parse SQLite ? RawRFIDReading records
   ?
4. Process readings
   ?
5. Match EPC ? Participant.RFIDTag
   ?
6. Create ReadingCheckpointAssignment (if checkpoint specified)
   ?
7. Mark readings as Success/Invalid
   ?
8. Update UploadBatch status
```

---

## ?? Important Notes

### Prerequisites
1. ? Participants must have `RFIDTag` field populated
2. ? SQLite file must follow expected schema
3. ? Valid checkpoint ID if checkpoint assignment desired

### Error Handling
- **Unlinked EPCs**: RFID tags not found in participant records
- **Weak Signals**: RSSI below -75 dBm threshold
- **Duplicate Files**: Same file (by hash) already uploaded
- **Invalid Data**: Missing required fields or corrupt SQLite

### Performance Considerations
- Batch processing for large files
- Transaction support for data integrity
- Efficient bulk inserts
- Indexed queries for fast lookups

---

## ?? Testing Checklist

- [ ] Upload SQLite file with valid RFID readings
- [ ] Process readings and verify participant linking
- [ ] Test timezone conversion (UTC vs local)
- [ ] Verify duplicate file detection works
- [ ] Test with unmatched EPCs (not in participants)
- [ ] Verify weak signal filtering (RSSI < -75)
- [ ] Test checkpoint assignment creation
- [ ] Verify audit trail is correct
- [ ] Test authorization (SuperAdmin/Admin only)
- [ ] Handle invalid/corrupt SQLite files gracefully

---

## ?? Database Schema Diagram

```
UploadBatches (Batch metadata)
    ? (1:Many)
RawRFIDReadings (Individual tag reads)
    ? (1:Many)
ReadingCheckpointAssignments (Links to checkpoints)
    ?
Checkpoints (Race checkpoints)

RawRFIDReadings.Epc ? Participant.RFIDTag (Linking)
```

---

## ?? Next Steps

1. **Run Migrations**: Create database tables
2. **Test Upload**: Try uploading a sample SQLite file
3. **Verify Processing**: Ensure readings link to participants
4. **Add Advanced Features** (Future):
   - Deduplication logic (remove duplicate readings within time window)
   - Manual review queue for unlinked EPCs
   - Automatic checkpoint detection via time gaps
   - Split time calculation
   - Results generation

---

## ?? Support

For issues or questions:
- Check logs in `_logger` for detailed error information
- Verify participants have `RFIDTag` populated
- Ensure SQLite file schema matches expected format
- Contact: support@runnatics.com
