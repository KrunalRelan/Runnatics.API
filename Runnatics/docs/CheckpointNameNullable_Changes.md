# Checkpoint Name Field - Made Nullable

## Summary
Made the `Name` field optional (nullable) in the Checkpoint entity to support scenarios where a checkpoint doesn't need a name when a Parent Checkpoint is selected.

## Changes Made

### 1. Entity Model - `Checkpoint.cs`
- ? Removed `[Required]` attribute from `Name` property
- ? Changed `string Name` to `string? Name`
- ? Removed default value initialization

### 2. Request DTO - `CheckpointRequest.cs`
- ? Removed `[Required]` attribute from `Name` property
- ? Changed `string Name` to `string? Name`
- ? Added custom error message for MaxLength validation
- ? Removed default value initialization

### 3. Response DTO - `CheckpointResponse.cs`
- ? Changed `string Name` to `string? Name` for consistency

### 4. EF Configuration - `CheckpointConfiguration.cs`
- ? Changed `IsRequired()` to `IsRequired(false)` for Name property

### 5. Database Migration
- ? Created SQL migration script: `MakeCheckpointNameNullable.sql`

## Database Migration

Run this SQL script to update the database:

```sql
ALTER TABLE [dbo].[Checkpoints]
ALTER COLUMN [Name] NVARCHAR(100) NULL;
```

Or use EF Core migrations:

```bash
cd C:\repositories\Runnatics.API\Runnatics\src\Runnatics.Data.EF
dotnet ef migrations add MakeCheckpointNameNullable --startup-project ..\Runnatics.Api
dotnet ef database update --startup-project ..\Runnatics.Api
```

## Business Logic Notes

- **Name is now optional** when creating or updating checkpoints
- **Parent checkpoint scenario**: When `ParentDeviceId` is provided, the checkpoint can use the parent's identity instead of requiring its own name
- **Validation**: MaxLength of 100 characters still applies when a name is provided
- **Service layer**: No changes needed - the service already handles nullable fields correctly

## API Impact

### Request Example (Name optional):
```json
{
  "deviceId": "encrypted-device-id",
  "parentDeviceId": "encrypted-parent-id",
  "distanceFromStart": 5.5,
  "isMandatory": true
}
```

### Response Example:
```json
{
  "id": "encrypted-id",
  "eventId": "encrypted-event-id",
  "raceId": "encrypted-race-id",
  "name": null,
  "distanceFromStart": 5.5,
  "deviceId": "encrypted-device-id",
  "parentDeviceId": "encrypted-parent-id",
  "isMandatory": true,
  "deviceName": "Start Gate",
  "parentDeviceName": "Main Timer"
}
```

## Testing Recommendations

1. ? Test creating checkpoint with name
2. ? Test creating checkpoint without name (null)
3. ? Test creating checkpoint with parent device and no name
4. ? Test updating checkpoint to remove name
5. ? Test validation: name exceeds 100 characters
6. ? Test existing checkpoints still work after migration

## Backward Compatibility

- ? Existing checkpoints with names will continue to work
- ? API remains backward compatible - name field is still accepted
- ? No breaking changes to existing API contracts
- ?? Consumers should handle null values for the `name` field in responses
