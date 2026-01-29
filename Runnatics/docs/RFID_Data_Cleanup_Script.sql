-- =====================================================
-- RFID Data Cleanup and Device-Checkpoint Mapping Fix
-- =====================================================
-- This script clears incorrectly processed RFID data and helps configure device-to-checkpoint mappings
-- Run this when checkpoint times are incorrect due to wrong device assignments

-- STEP 1: Backup existing data (RECOMMENDED!)
-- Before running cleanup, backup your data:
/*
SELECT * INTO UploadBatch_Backup FROM UploadBatch WHERE EventId = @EventId
SELECT * INTO RawRFIDReading_Backup FROM RawRFIDReading WHERE BatchId IN (SELECT Id FROM UploadBatch WHERE EventId = @EventId)
SELECT * INTO ReadingCheckpointAssignment_Backup FROM ReadingCheckpointAssignment 
SELECT * INTO ReadNormalized_Backup FROM ReadNormalized WHERE EventId = @EventId
SELECT * INTO Results_Backup FROM Results WHERE EventId = @EventId
*/

-- =====================================================
-- PARAMETERS - UPDATE THESE VALUES
-- =====================================================
DECLARE @EventId INT = 1;     -- Your Event ID
DECLARE @RaceId INT = 1;      -- Your Race ID

-- =====================================================
-- SECTION 1: VIEW CURRENT DEVICE-CHECKPOINT MAPPINGS
-- =====================================================
PRINT '=== Current Device-Checkpoint Mappings ==='
SELECT 
    c.Id AS CheckpointId,
    c.Name AS CheckpointName,
    c.DistanceFromStart,
    c.DeviceId,
    d.Name AS DeviceName,
    d.DeviceId AS DeviceSerial,
    c.ParentDeviceId,
    pd.Name AS ParentDeviceName
FROM Checkpoint c
LEFT JOIN Device d ON c.DeviceId = d.Id
LEFT JOIN Device pd ON c.ParentDeviceId = pd.Id
WHERE c.EventId = @EventId 
  AND c.RaceId = @RaceId 
  AND c.IsActive = 1 
  AND c.IsDeleted = 0
ORDER BY c.DistanceFromStart;

-- =====================================================
-- SECTION 2: VIEW PROCESSED BATCHES AND ASSIGNMENTS
-- =====================================================
PRINT '=== Upload Batches and Checkpoint Assignments ==='
SELECT 
    ub.Id AS BatchId,
    ub.OriginalFileName,
    ub.DeviceId AS DeviceSerial,
    ub.ExpectedCheckpointId,
    c.Name AS AssignedCheckpoint,
    ub.TotalReadings,
    ub.Status,
    ub.CreatedDate
FROM UploadBatch ub
LEFT JOIN Checkpoint c ON ub.ExpectedCheckpointId = c.Id
WHERE ub.EventId = @EventId 
  AND ub.RaceId = @RaceId
ORDER BY ub.CreatedDate;

-- =====================================================
-- SECTION 3: VIEW READING DISTRIBUTION BY CHECKPOINT
-- =====================================================
PRINT '=== Reading Distribution by Checkpoint ==='
SELECT 
    c.Id AS CheckpointId,
    c.Name AS CheckpointName,
    COUNT(DISTINCT rn.ParticipantId) AS ParticipantCount,
    COUNT(rn.Id) AS TotalReadings,
    MIN(rn.ChipTime) AS EarliestTime,
    MAX(rn.ChipTime) AS LatestTime
FROM ReadNormalized rn
INNER JOIN Checkpoint c ON rn.CheckpointId = c.Id
WHERE rn.EventId = @EventId
GROUP BY c.Id, c.Name, c.DistanceFromStart
ORDER BY c.DistanceFromStart;

-- =====================================================
-- SECTION 4: CLEANUP - DELETE INCORRECTLY PROCESSED DATA
-- =====================================================
-- ?? WARNING: This will delete processed RFID data!
-- Only run this if you need to reprocess everything

-- Uncomment the lines below to execute cleanup
/*
BEGIN TRANSACTION;

PRINT '=== Cleaning up incorrectly processed RFID data ==='

-- Delete Results
DELETE FROM Results 
WHERE EventId = @EventId AND RaceId = @RaceId;
PRINT 'Deleted Results'

-- Delete normalized readings
DELETE FROM ReadNormalized 
WHERE EventId = @EventId;
PRINT 'Deleted ReadNormalized'

-- Delete checkpoint assignments
DELETE FROM ReadingCheckpointAssignment 
WHERE ReadingId IN (
    SELECT r.Id 
    FROM RawRFIDReading r
    INNER JOIN UploadBatch ub ON r.BatchId = ub.Id
    WHERE ub.EventId = @EventId AND ub.RaceId = @RaceId
);
PRINT 'Deleted ReadingCheckpointAssignment'

-- Reset raw readings to Pending status
UPDATE RawRFIDReading
SET ProcessResult = 'Pending',
    ProcessedAt = NULL,
    AssignmentMethod = NULL,
    Notes = NULL
WHERE BatchId IN (
    SELECT Id FROM UploadBatch 
    WHERE EventId = @EventId AND RaceId = @RaceId
);
PRINT 'Reset RawRFIDReading to Pending'

-- Reset upload batches to uploaded status
UPDATE UploadBatch
SET Status = 'uploaded',
    ProcessingStartedAt = NULL,
    ProcessingCompletedAt = NULL
WHERE EventId = @EventId AND RaceId = @RaceId;
PRINT 'Reset UploadBatch to uploaded'

COMMIT TRANSACTION;
PRINT '=== Cleanup completed successfully ==='
*/

-- =====================================================
-- SECTION 5: CONFIGURE DEVICE-TO-CHECKPOINT MAPPINGS
-- =====================================================
-- Update these statements with your actual device serials and checkpoint IDs

-- Example: Map box15 (serial: 0016251292ae) to Start checkpoint
/*
UPDATE Checkpoint 
SET DeviceId = (
    SELECT Id FROM Device 
    WHERE DeviceId = '0016251292ae' 
      AND IsActive = 1 
      AND IsDeleted = 0
)
WHERE Name = 'Start' 
  AND EventId = @EventId 
  AND RaceId = @RaceId 
  AND IsActive = 1 
  AND IsDeleted = 0;

-- Map Box-16 (serial: 0016251292a1) to 5KM checkpoint
UPDATE Checkpoint 
SET DeviceId = (
    SELECT Id FROM Device 
    WHERE DeviceId = '0016251292a1' 
      AND IsActive = 1 
      AND IsDeleted = 0
)
WHERE Name = '5KM' 
  AND EventId = @EventId 
  AND RaceId = @RaceId 
  AND IsActive = 1 
  AND IsDeleted = 0;

-- Map Box-19 (serial: 00162512dbb0) to 10KM checkpoint
UPDATE Checkpoint 
SET DeviceId = (
    SELECT Id FROM Device 
    WHERE DeviceId = '00162512dbb0' 
      AND IsActive = 1 
      AND IsDeleted = 0
)
WHERE Name = '10KM' 
  AND EventId = @EventId 
  AND RaceId = @RaceId 
  AND IsActive = 1 
  AND IsDeleted = 0;

-- Map box_24 (serial: 001625135f24) to Finish checkpoint
UPDATE Checkpoint 
SET DeviceId = (
    SELECT Id FROM Device 
    WHERE DeviceId = '001625135f24' 
      AND IsActive = 1 
      AND IsDeleted = 0
)
WHERE Name = 'Finish' 
  AND EventId = @EventId 
  AND RaceId = @RaceId 
  AND IsActive = 1 
  AND IsDeleted = 0;
*/

-- =====================================================
-- SECTION 6: VERIFY DEVICE SERIAL NUMBERS
-- =====================================================
-- Check if your devices exist in the database
PRINT '=== Available Devices ==='
SELECT 
    Id,
    Name,
    DeviceId AS Serial,
    TenantId,
    CreatedDate
FROM Device
WHERE IsActive = 1 AND IsDeleted = 0
ORDER BY Name;

-- =====================================================
-- SECTION 7: CREATE MISSING DEVICES (if needed)
-- =====================================================
-- If devices don't exist, create them first
/*
DECLARE @UserId INT = 1;  -- Update with your user ID
DECLARE @TenantId INT = 1;  -- Update with your tenant ID

-- Create box15 device
IF NOT EXISTS (SELECT 1 FROM Device WHERE DeviceId = '0016251292ae')
BEGIN
    INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
    VALUES (@TenantId, 'Box-15', '0016251292ae', @UserId, GETUTCDATE(), 1, 0);
    PRINT 'Created Device: Box-15'
END

-- Create Box-16 device  
IF NOT EXISTS (SELECT 1 FROM Device WHERE DeviceId = '0016251292a1')
BEGIN
    INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
    VALUES (@TenantId, 'Box-16', '0016251292a1', @UserId, GETUTCDATE(), 1, 0);
    PRINT 'Created Device: Box-16'
END

-- Create Box-19 device
IF NOT EXISTS (SELECT 1 FROM Device WHERE DeviceId = '00162512dbb0')
BEGIN
    INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
    VALUES (@TenantId, 'Box-19', '00162512dbb0', @UserId, GETUTCDATE(), 1, 0);
    PRINT 'Created Device: Box-19'
END

-- Create box_24 device
IF NOT EXISTS (SELECT 1 FROM Device WHERE DeviceId = '001625135f24')
BEGIN
    INSERT INTO Device (TenantId, Name, DeviceId, CreatedBy, CreatedDate, IsActive, IsDeleted)
    VALUES (@TenantId, 'Box-24', '001625135f24', @UserId, GETUTCDATE(), 1, 0);
    PRINT 'Created Device: Box-24'
END
*/

-- =====================================================
-- SECTION 8: FINAL VERIFICATION
-- =====================================================
PRINT '=== Verification: Checkpoints with Device Mappings ==='
SELECT 
    c.Id AS CheckpointId,
    c.Name AS CheckpointName,
    c.DistanceFromStart,
    c.DeviceId,
    d.DeviceId AS DeviceSerial,
    d.Name AS DeviceName,
    CASE 
        WHEN c.DeviceId IS NULL OR c.DeviceId = 0 THEN '? NO DEVICE MAPPED'
        WHEN d.Id IS NULL THEN '? DEVICE NOT FOUND'
        ELSE '? MAPPED'
    END AS Status
FROM Checkpoint c
LEFT JOIN Device d ON c.DeviceId = d.Id
WHERE c.EventId = @EventId 
  AND c.RaceId = @RaceId 
  AND c.IsActive = 1 
  AND c.IsDeleted = 0
ORDER BY c.DistanceFromStart;

PRINT '=== Script completed ==='
PRINT 'Next steps:'
PRINT '1. Verify device-checkpoint mappings above show ? MAPPED status'
PRINT '2. If mappings are correct, run "Process Result" in the UI'
PRINT '3. Check the Participants tab to see checkpoint times'
