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

### 2026-03-31 — backend-agent — SupportQuery / Contact Us Feature

- **Branch**: `feature/OnlineReadingsFlow` (existing branch)
- **What was built**: Full support query feature — public Contact Us submission, admin list/detail/update/comment/email/delete endpoints
- **Files created**:
  - `db/scripts/SupportQuery_CreateTables_20260331.sql` — 4 tables + status seed data
  - `Runnatics.Models.Data/Entities/SupportQueryStatus.cs`
  - `Runnatics.Models.Data/Entities/SupportQueryType.cs`
  - `Runnatics.Models.Data/Entities/SupportQuery.cs`
  - `Runnatics.Models.Data/Entities/SupportQueryComment.cs`
  - `Runnatics.Data.EF/Config/SupportQueryStatusConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryTypeConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryConfiguration.cs`
  - `Runnatics.Data.EF/Config/SupportQueryCommentConfiguration.cs`
  - `Runnatics.Models.Client/Requests/Support/ContactUsRequestDto.cs`
  - `Runnatics.Models.Client/Requests/Support/AddCommentRequestDto.cs`
  - `Runnatics.Models.Client/Requests/Support/UpdateQueryRequestDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryListItemDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryDetailDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryCommentDto.cs`
  - `Runnatics.Models.Client/Responses/Support/SupportQueryCountsDto.cs`
  - `Runnatics.Services.Interface/ISupportQueryService.cs`
  - `Runnatics.Services/SupportQueryService.cs`
  - `Runnatics.Api/Controller/SupportQueryController.cs`
- **Files modified**:
  - `Runnatics.Data.EF/RaceSyncDbContext.cs` — added 4 DbSets + 4 ApplyConfiguration calls
  - `Runnatics.Services.Interface/IEmailService.cs` — added `SendAsync(string to, string subject, string body)`
  - `Runnatics.Api/Program.cs` — registered `ISupportQueryService → SupportQueryService`
- **Decisions made**:
  - SupportQuery/SupportQueryComment entities do NOT use AuditProperties owned type — these are support tickets with a simpler schema (CreatedAt/UpdatedAt directly on entity), as per explicit SQL spec
  - `AssignedToUserId = 0` in UpdateQueryRequestDto is treated as "unassign" (sets to null)
  - `LastUpdated` relative label is computed in service layer (days → hours → minutes)
  - `DeleteCommentAsync` is a hard delete (uses `repo.DeleteAsync(id)`) since comments have no AuditProperties
  - Admin user ID in AddComment is extracted from JWT `sub` claim in the controller
- **Pending**: IEmailService `SendAsync` implementation needs to be added to the concrete email service class
