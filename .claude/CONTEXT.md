# Runnatics.API — Shared Context

> **Every agent MUST read this file before starting any task and write to it after completing any task.**
> Document: what was built, file paths, decisions made, and what's pending.

---

## Project Overview

- **Platform**: .NET 10 race timing management system
- **Architecture**: N-Layer (Domain → Application → Infrastructure → API)
- **Database**: Azure SQL (NO EF Migrations — hand-written SQL only)
- **ORM**: EF Core with `IEntityTypeConfiguration` + `RaceSyncDbContext`
- **Auth**: JWT Bearer with multi-tenant claims (`tenantId`, `sub`, `role`)
- **Real-time**: SignalR via `RaceHub` at `/hubs/race`

---

## Key Architectural Decisions

| Decision | Detail |
|----------|--------|
| All public IDs encrypted | `IEncryptionService` (AES) — use `IdEncryptor`/`IdDecryptor` in AutoMapper |
| Every entity has `AuditProperties` | Owned type: `IsActive`, `IsDeleted`, `CreatedDate`, `CreatedBy`, `UpdatedDate`, `UpdatedBy` |
| No EF Migrations | Schema via SQL scripts. Never run `dotnet ef migrations add` |
| Multi-tenant | `TenantId` on most entities, set via `IUnitOfWork.SetTenantId()` |
| Soft delete only | Set `IsDeleted = true`, `IsActive = false` — never hard delete |
| Service error pattern | `SimpleServiceBase.HasError` / `ErrorMessage` — no exception throwing to controllers |
| Response wrapping | All API responses use `ResponseBase<T>` |
| Branch convention | `feature/{FeatureName}` from `master` |

---

## Layer Map

| Layer | Project | Key Classes |
|-------|---------|-------------|
| API | `Runnatics.Api` | Controllers, `Program.cs` |
| Application | `Runnatics.Services` | Service implementations, `AutoMapperMappingProfile`, `RaceHub` |
| Application (Interfaces) | `Runnatics.Services.Interface` | `ISimpleServiceBase`, service interfaces |
| Domain (Entities) | `Runnatics.Models.Data` | Entity classes, `AuditProperties`, `PagingList<T>` |
| Domain (Client) | `Runnatics.Models.Client` | Request/Response DTOs, `ResponseBase<T>` |
| Infrastructure (EF) | `Runnatics.Data.EF` | `RaceSyncDbContext`, `IEntityTypeConfiguration` implementations |
| Infrastructure (Repos) | `Runnatics.Repositories.EF` | `GenericRepository<T>`, `UnitOfWork<C>` |
| Infrastructure (Interfaces) | `Runnatics.Repositories.Interface` | `IGenericRepository<T>`, `IUnitOfWork<C>` |

---

## Existing Entities (34 total)

`Organization`, `User`, `UserSession`, `UserInvitation`, `PasswordResetToken`, `Event`, `EventSettings`, `EventOrganizer`, `LeaderboardSettings`, `Race`, `RaceSettings`, `Participant`, `ParticipantStaging`, `Checkpoint`, `Device`, `ReaderDevice`, `ReaderAssignment`, `Chip`, `ChipAssignment`, `ReadRaw`, `ReadNormalized`, `RawRFIDReading`, `UploadBatch`, `ReadingCheckpointAssignment`, `ImportBatch`, `Results`, `SplitTimes`, `Notification`, `CertificateTemplate`, `CertificateField`

## Existing Services

`AuthenticationService`, `EventsService`, `RaceService`, `ParticipantImportService`, `CheckpointService`, `DevicesService`, `RFIDImportService`, `ResultsService`, `DashboardService`, `CertificatesService`, `EventOrganizerService`, `UserContextService`, `EncryptionService`, `R700CommunicationService`, `RaceReaderService`, `OnlineTagIngestionService`

## Existing Controllers

`AuthenticationController`, `EventsController`, `ParticipantsController`, `RacesController`, `CheckpointsController`, `DevicesController`, `RFIDController`, `RfidWebhookController`, `ResultsController`, `DashboardController`, `CertificatesController`, `EvenOrganizerController`, `RaceControlController`

---

## Session Log

_Use this section to log what each agent built during the current session._

<!--
FORMAT:
### [Date] — [Agent] — [Feature/Task]
- **What was built**: ...
- **Files created/modified**: ...
- **Decisions made**: ...
- **Pending**: ...
-->
