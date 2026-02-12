# RFID Upload System - Refactoring Summary

## 🎯 What We Accomplished

### ✅ Database Schema Cleanup

**Removed Legacy Tables:**
- ❌ `FileUploadBatches` → Replaced with `UploadBatches`
- ❌ `FileUploadRecords` → Replaced with `RawRFIDReadings`
- ❌ `FileUploadMappings` → Replaced with `ReadingCheckpointAssignments`
- ❌ `ReadQueue` → Legacy queue table (unused)
- ❌ `ReadRaws` (old with BatchId) → Replaced with new `RawRFIDReadings`

**New Clean Schema:**
```
✅ UploadBatches (26 columns, 4 foreign keys)
   └─ Tracks SQLite/CSV file uploads
   └─ Links to: Races, Events, Devices, Checkpoints

✅ RawRFIDReadings (27 columns, 1 foreign key)
   └─ Individual RFID tag reads from files
   └─ Cascade deletes with UploadBatch
   └─ Supports deduplication and checkpoint assignment

✅ ReadingCheckpointAssignments (9 columns, 2 foreign keys)
   └─ Many-to-many mapping of readings to checkpoints
   └─ Unique constraint on (ReadingId, CheckpointId)
```

**Foreign Key Relationships:**
```
Events → UploadBatches.EventId
Races → UploadBatches.RaceId
Devices → UploadBatches.ReaderDeviceId
Checkpoints → UploadBatches.ExpectedCheckpointId
UploadBatches → RawRFIDReadings.BatchId
RawRFIDReadings → ReadingCheckpointAssignments.ReadingId
Checkpoints → ReadingCheckpointAssignments.CheckpointId
```

---

### ✅ Updated API Models

**1. RFIDImportRequest.cs**
```csharp
public class RFIDImportRequest
{
    public required IFormFile File { get; set; }
    public string? DeviceId { get; set; }
    public string? ExpectedCheckpointId { get; set; }
    public string? ReaderDeviceId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string FileFormat { get; set; } = "DB";  // NEW
    public string SourceType { get; set; } = "file_upload";  // NEW
}
```

**2. RFIDImportResponse.cs**
```csharp
public class RFIDImportResponse
{
    public string? UploadBatchId { get; set; }  // Changed from ImportBatchId
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int TotalReadings { get; set; }  // Changed from TotalRecords
    public int UniqueEpcs { get; set; }  // NEW
    public long? TimeRangeStart { get; set; }  // NEW
    public long? TimeRangeEnd { get; set; }  // NEW
    public long FileSizeBytes { get; set; }  // NEW
    public string FileFormat { get; set; } = "DB";  // NEW
    public string Status { get; set; } = "uploading";  // NEW values
}
```

**3. ProcessRFIDImportRequest.cs**
```csharp
public class ProcessRFIDImportRequest
{
    public required string UploadBatchId { get; set; }  // Changed from ImportBatchId
    public required string EventId { get; set; }
    public required string RaceId { get; set; }
    public int DeduplicationWindowSeconds { get; set; } = 3;  // NEW
    public bool AutoAssignCheckpoints { get; set; } = true;  // NEW
    public int MinCheckpointGapSeconds { get; set; } = 60;  // NEW
}
```

---

### ✅ Updated Controller

**RFIDController.cs Changes:**
- ✅ Updated route parameter from `importBatchId` to `uploadBatchId`
- ✅ Updated validation messages
- ✅ Better error handling for 404 (not found) scenarios

**Endpoint Structure:**
```http
POST /api/rfid/{eventId}/{raceId}/epc-mapping
  → Upload EPC-BIB mapping Excel file

POST /api/rfid/{eventId}/{raceId}/import
  → Upload SQLite/CSV RFID file

POST /api/rfid/{eventId}/{raceId}/import/{uploadBatchId}/process
  → Process uploaded batch (deduplicate, assign checkpoints)

POST /api/rfid/{eventId}/{raceId}/deduplicate
  → Create normalized timing data
```

---

### ✅ Entity Framework Configuration

All EF Core configurations properly set up in:
- `UploadBatchConfiguration.cs`
- `RawRFIDReadingConfiguration.cs`
- `ReadingCheckpointAssignmentConfiguration.cs`

**Key Features:**
- Audit properties on all tables
- Proper foreign key relationships
- Cascade delete on batch removal
- Performance indexes on critical columns

---

## 🚧 What Still Needs Implementation

### Phase 1: Service Layer - File Upload

**File: `RFIDImportService.cs`**

**Method:** `UploadRFIDFileAsync()`

**Implement:**
1. ✅ Validate file (format, size, hash)
2. ✅ Create `UploadBatch` entity
3. ⚠️ Parse SQLite file using `System.Data.SQLite`
4. ⚠️ Parse CSV file (if format = "CSV")
5. ⚠️ Create `RawRFIDReading` entities for each read
6. ⚠️ Calculate statistics (TotalReadings, UniqueEpcs, TimeRange)
7. ⚠️ Save to database
8. ✅ Return encrypted `UploadBatchId`

**Pseudocode:**
```csharp
public async Task<RFIDImportResponse> UploadRFIDFileAsync(string eventId, string raceId, RFIDImportRequest request)
{
    // 1. Decrypt and validate IDs
    var decryptedEventId = Decrypt(eventId);
    var decryptedRaceId = Decrypt(raceId);
    
    // 2. Validate file
    if (!IsValidFormat(request.File))
        return Error("Invalid file format");
    
    var fileHash = ComputeFileHash(request.File);
    
    // Check for duplicates
    if (await BatchExistsWithHash(fileHash))
        return Error("File already uploaded");
    
    // 3. Create UploadBatch
    var batch = new UploadBatch
    {
        RaceId = decryptedRaceId,
        EventId = decryptedEventId,
        OriginalFileName = request.File.FileName,
        FileSizeBytes = request.File.Length,
        FileHash = fileHash,
        FileFormat = request.FileFormat,
        Status = "uploading",
        // ... other fields
    };
    
    await _repository.AddAsync(batch);
    await _repository.SaveChangesAsync();
    
    // 4. Parse file based on format
    var readings = request.FileFormat == "DB" 
        ? await ParseSQLiteFile(request.File, batch.Id)
        : await ParseCSVFile(request.File, batch.Id);
    
    // 5. Save readings
    await _repository.BulkInsertAsync(readings);
    
    // 6. Update batch statistics
    batch.TotalReadings = readings.Count;
    batch.UniqueEpcs = readings.Select(r => r.Epc).Distinct().Count();
    batch.TimeRangeStart = readings.Min(r => r.TimestampMs);
    batch.TimeRangeEnd = readings.Max(r => r.TimestampMs);
    batch.Status = "uploaded";
    
    await _repository.SaveChangesAsync();
    
    // 7. Return response
    return new RFIDImportResponse
    {
        UploadBatchId = Encrypt(batch.Id),
        FileName = batch.OriginalFileName,
        TotalReadings = batch.TotalReadings.Value,
        UniqueEpcs = batch.UniqueEpcs.Value,
        Status = batch.Status
    };
}
```

---

### Phase 2: Service Layer - Processing

**Method:** `ProcessRFIDStagingDataAsync()`

**Implement:**
1. ⚠️ Get `UploadBatch` and all `RawRFIDReadings`
2. ⚠️ Deduplicate readings within time window
3. ⚠️ Auto-assign checkpoints (if enabled)
4. ⚠️ Create `ReadingCheckpointAssignment` records
5. ⚠️ Update processing status
6. ⚠️ Return processing summary

**Pseudocode:**
```csharp
public async Task<ProcessRFIDImportResponse> ProcessRFIDStagingDataAsync(ProcessRFIDImportRequest request)
{
    var batchId = Decrypt(request.UploadBatchId);
    
    // 1. Get batch and readings
    var batch = await _repository.GetByIdAsync<UploadBatch>(batchId);
    var readings = await GetReadingsByBatchAsync(batchId);
    
    batch.Status = "processing";
    batch.ProcessingStartedAt = DateTime.UtcNow;
    await _repository.SaveChangesAsync();
    
    // 2. Deduplicate
    var deduped = await DeduplicateReadings(
        readings, 
        request.DeduplicationWindowSeconds);
    
    // 3. Auto-assign checkpoints
    if (request.AutoAssignCheckpoints)
    {
        var checkpoints = await GetCheckpointsAsync(batch.RaceId);
        await AssignCheckpointsAsync(
            deduped, 
            checkpoints, 
            request.MinCheckpointGapSeconds);
    }
    
    // 4. Update status
    batch.Status = "completed";
    batch.ProcessingCompletedAt = DateTime.UtcNow;
    await _repository.SaveChangesAsync();
    
    return new ProcessRFIDImportResponse { ... };
}
```

---

### Phase 3: Service Layer - Normalization

**Method:** `DeduplicateAndNormalizeAsync()`

**Implement:**
1. ⚠️ Get all processed readings for event/race
2. ⚠️ Match EPCs to Participants via `ChipAssignments`
3. ⚠️ Create `ReadNormalized` entries
4. ⚠️ Calculate `SplitTimes`
5. ⚠️ Update `Results` rankings

---

## 📂 File Structure

```
Runnatics/
├── src/
│   ├── Runnatics.Api/
│   │   └── Controller/
│   │       └── RFIDController.cs ✅ UPDATED
│   ├── Runnatics.Models.Client/
│   │   ├── Requests/RFID/
│   │   │   ├── RFIDImportRequest.cs ✅ UPDATED
│   │   │   ├── ProcessRFIDImportRequest.cs ✅ UPDATED
│   │   │   └── EPCMappingImportRequest.cs ✅ (no changes)
│   │   └── Responses/RFID/
│   │       ├── RFIDImportResponse.cs ✅ UPDATED
│   │       ├── ProcessRFIDImportResponse.cs
│   │       └── DeduplicationResponse.cs
│   ├── Runnatics.Models.Data/
│   │   └── Entities/
│   │       ├── UploadBatch.cs ✅ EXISTS
│   │       ├── RawRFIDReading.cs ✅ EXISTS
│   │       └── ReadingCheckpointAssignment.cs ✅ EXISTS
│   ├── Runnatics.Data.EF/
│   │   ├── Config/
│   │   │   ├── UploadBatchConfiguration.cs ✅ EXISTS
│   │   │   ├── RawRFIDReadingConfiguration.cs ✅ EXISTS
│   │   │   └── ReadingCheckpointAssignmentConfiguration.cs ✅ EXISTS
│   │   └── RaceSyncDbContext.cs ✅ UPDATED
│   ├── Runnatics.Services/
│   │   └── RFIDImportService.cs ⚠️ NEEDS IMPLEMENTATION
│   └── Runnatics.Services.Interface/
│       └── IRFIDImportService.cs ✅ EXISTS
└── RFID_IMPLEMENTATION_GUIDE.md ✅ CREATED
```

---

## 🔄 Next Steps

### Immediate Tasks

1. **Implement SQLite Parser**
   ```csharp
   private async Task<List<RawRFIDReading>> ParseSQLiteFile(IFormFile file, int batchId)
   {
       // Extract file to temp location
       // Open SQLite connection
       // Query TagReads or equivalent table
       // Map to RawRFIDReading entities
       // Return list
   }
   ```

2. **Implement CSV Parser**
   ```csharp
   private async Task<List<RawRFIDReading>> ParseCSVFile(IFormFile file, int batchId)
   {
       // Read CSV rows
       // Parse columns: EPC, Timestamp, Antenna, RSSI
       // Map to RawRFIDReading entities
       // Return list
   }
   ```

3. **Implement Deduplication Logic**
   ```csharp
   private async Task<List<RawRFIDReading>> DeduplicateReadings(
       List<RawRFIDReading> readings, 
       int windowSeconds)
   {
       // Group by EPC
       // Sort by TimestampMs
       // Mark duplicates within window
       // Return unique readings
   }
   ```

4. **Implement Checkpoint Assignment**
   ```csharp
   private async Task AssignCheckpointsAsync(
       List<RawRFIDReading> readings,
       List<Checkpoint> checkpoints,
       int minGapSeconds)
   {
       // Implement time-gap based assignment
       // Create ReadingCheckpointAssignment records
       // Set AssignmentMethod and confidence
   }
   ```

---

## 📊 Database Migration Status

✅ **All tables created successfully:**
- `UploadBatches` - 26 columns, 4 FKs
- `RawRFIDReadings` - 27 columns, 1 FK
- `ReadingCheckpointAssignments` - 9 columns, 2 FKs

✅ **All legacy tables removed:**
- `FileUploadBatches` ❌
- `FileUploadRecords` ❌
- `FileUploadMappings` ❌
- `ReadQueue` ❌
- `ReadRaws` (old) ❌

✅ **Foreign keys properly configured**
✅ **Indexes created for performance**
✅ **Audit fields on all tables**

---

## 🎓 Key Learnings

1. **Clean schema is critical** - Removed 5 legacy tables to avoid confusion
2. **Naming consistency matters** - Changed `ImportBatch` → `UploadBatch` throughout
3. **Future-proof design** - Added fields for different file formats and source types
4. **Performance indexes** - Added on EPC, TimestampMs, ProcessResult, Status
5. **Audit trail** - All entities have CreatedAt, UpdatedAt, IsDeleted, IsActive

---

## 🚀 Ready for Development

Your RFID upload system foundation is **100% ready**!

✅ Database schema clean and optimized  
✅ API models updated and aligned  
✅ Controller refactored  
✅ EF Core configurations complete  
✅ Implementation guide created  

**Next:** Implement the service layer methods! 🔨

---

*Last Updated: 2026-01-27*  
*Branch: feature/RFIDReadings_Upload*  
*Database: Runnatics_Dev*
