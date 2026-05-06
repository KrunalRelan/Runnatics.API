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

### 2026-04-15 — backend-agent — Testing Feedback: Event/Participant/Race Fixes

- **What was built**: 5 feedback items addressed from testing
- **Files modified**:
  - `Runnatics.Models.Client/Requests/Events/EventRequest.cs` — removed `TimeZone` and `Status` fields (set server-side)
  - `Runnatics.Services/EventsService.cs` — `CreateEventEntity` now sets `Status = Draft`, `TimeZone = "Asia/Kolkata"` server-side
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` — ignore Status/TimeZone/MaxParticipants/RegistrationDeadline on EventRequest→Event; ignore TotalParticipants/EncodedEpcCount on Race→RaceResponse
  - `Runnatics.Models.Data/Entities/Participant.cs` — added `ManualDistance` (decimal?) and `LoopCount` (int?)
  - `Runnatics.Models.Data/Entities/Results.cs` — added `ManualFinishTimeMs` (long?) for admin-entered finish time
  - `Runnatics.Models.Client/Responses/Races/RaceResponse.cs` — added `TotalParticipants` and `EncodedEpcCount`
  - `Runnatics.Models.Client/Responses/Participants/ParticipantSearchReponse.cs` — added `List<CheckpointTimeDto>? Checkpoints`
  - `Runnatics.Services.Interface/IParticipantImportService.cs` — added `UpdateParticipantExtendedAsync` and `DeleteParticipantAsync`
  - `Runnatics.Services/ParticipantImportService.cs` — implemented new methods; `PopulateCheckpointTimesAsync` now also builds `Checkpoints` list
  - `Runnatics.Services/RaceService.cs` — `LoadRaceResponsesAsync` now computes TotalParticipants and EncodedEpcCount via two GROUP BY queries
  - `Runnatics.Api/Controller/ParticipantsController.cs` — added `PUT ~/api/races/{raceId}/participants/{participantId}` and `DELETE ~/api/races/{raceId}/participants/{participantId}`
- **Files created**:
  - `Runnatics.Models.Client/Responses/Participants/CheckpointTimeDto.cs` — structured checkpoint time DTO
  - `Runnatics.Models.Client/Requests/Participant/UpdateParticipantRequest.cs` — extended update DTO with RunStatus/DisqualificationReason/ManualTime/ManualDistance/LoopCount/RaceId
  - `db/scripts/Participants_AddManualFields_20260415.sql` — ALTER TABLE scripts for new columns
- **Decisions made**:
  - Location fields (VenueName, City, Country) were already nullable/optional in EventRequest — no change needed
  - RunStatus "OK" maps to Participant.Status = "Registered" (or "Finished" in Results); other values pass through
  - Race reassignment in UpdateParticipantExtended soft-deletes the old record and creates a new one in the target race
  - Checkpoint times in participant search now return both `CheckpointTimes` (dictionary, backward-compat) and `Checkpoints` (ordered list)
  - EPC count uses ChipAssignment → Participant join since ChipAssignment has no direct RaceId
- **Pending**: Run `db/scripts/Participants_AddManualFields_20260415.sql` against Azure SQL to add the new columns

### 2026-04-08 — backend-agent — Generate & Download Participant Certificate

- **What was built**: `GET /api/certificates/participant/{participantId}/download` — generates a filled PNG certificate for a participant using SkiaSharp
- **Files modified**:
  - `Runnatics.Services/Runnatics.Services.csproj` — added SkiaSharp 2.88.8 + SkiaSharp.NativeAssets.Linux 2.88.8
  - `Runnatics.Services.Interface/ICertificatesService.cs` — added `GenerateParticipantCertificateAsync`
  - `Runnatics.Services/CertificatesService.cs` — added IHttpClientFactory to constructor; new public method + 6 private helpers
  - `Runnatics.Api/Controller/CertificatesController.cs` — added `DownloadParticipantCertificate` action
- **Decisions made**:
  - Template selection: race-specific → IsDefault → event-wide (RaceId = null) — mirrors `GetTemplateByRaceAsync`
  - `Results.FinishTime` (ms) → ChipTime; `Results.GunTime` (ms) → GunTime; formatted as `HH:MM:SS` via TotalHours
  - `RaceCategory` = `Race.Title`; `Category` = `Participant.AgeCategory`
  - `Photo` field skipped — no photo property on `Participant`; `CustomText` renders `field.Content` verbatim
  - Background: base64 `BackgroundImageData` preferred over URL (fetched via IHttpClientFactory)
  - All IDs accept encrypted strings via existing `TryParseOrDecrypt`
- **Pending**: Photo field support requires adding a photo URL property to the Participant entity
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

### 2026-04-16 — backend-agent — Public API: DTOs (Prompt 1)

- **What was built**: Public-facing DTO layer for the Runnatics marketing website
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicEventSummaryDto.cs` — summary card for event listings
  - `Runnatics.Models.Client/Public/PublicEventDetailDto.cs` — extends summary; adds Races, FullDescription, Schedule, RouteMapUrl, RegistrationDeadline, ContactEmail
  - `Runnatics.Models.Client/Public/PublicRaceCategoryDto.cs` — race info for public display; Price is null (Race entity has no Price column yet)
  - `Runnatics.Models.Client/Public/PublicResultDto.cs` — race result with splits; GunTime/NetTime are TimeSpan? converted from milliseconds
  - `Runnatics.Models.Client/Public/PublicSplitDto.cs` — checkpoint split time; CheckpointName from SplitTimes.ToCheckpoint.Name
  - `Runnatics.Models.Client/Public/PublicGalleryImageDto.cs` — gallery image placeholder (no GalleryImage entity yet)
  - `Runnatics.Models.Client/Public/PublicPagedResultDto.cs` — paged wrapper with TotalPages/HasNext/HasPrevious computed props
  - `Runnatics.Models.Client/Requests/Public/PublicContactRequestDto.cs` — contact form with DataAnnotations
- **Decisions made**:
  - `Event.Slug` exists — no workaround needed
  - `PagingList<T>` only has TotalCount (extends List<T>) — created new `PublicPagedResultDto<T>` with full pagination metadata
  - No encrypted IDs on public DTOs (plain int/slug — public data, no security concern)
  - `Race.Price` does not exist — property left nullable with comment; add column when ready
  - `PublicEventDetailDto` inherits `PublicEventSummaryDto` to avoid duplication
- **Pending**: Prompts 3–5 (controller, CORS, verify/build)

### 2026-04-16 — backend-agent — Public API: Service Methods (Prompt 2)

- **What was added**: New methods to existing service interfaces/implementations only (no new classes)
- **Files modified**:
  - `Runnatics.Services.Interface/IEventsService.cs` — added `GetPublicEventsAsync(bool isPast, string? city, string? searchQuery, int page, int pageSize)` and `GetPublicEventBySlugAsync(string slug)`; alias `DataPagingList` avoids collision with client `PagingList<T>`
  - `Runnatics.Services/EventsService.cs` — implemented both methods in `#region Public (no-auth) methods`; list uses filtered `.Include(e => e.Races)`, detail uses `.ThenInclude(r => r.Participants)` for per-race counts
  - `Runnatics.Services.Interface/IResultsService.cs` — added `GetPublicResultsAsync(int eventId, string? raceName, string? searchQuery, string? gender, int page, int pageSize)` returning `DataResultsPagingList`
  - `Runnatics.Services/ResultsService.cs` — implemented `GetPublicResultsAsync`; filters by eventId, race name, bib/name search, gender; includes `Participant`, `Race`, `Participant.SplitTimes → ToCheckpoint`
  - `Runnatics.Services.Interface/ISupportQueryService.cs` — added `CreatePublicQueryAsync(string name, string email, string? phone, string subject, string message, string? eventName)`
  - `Runnatics.Services/SupportQueryService.cs` — implemented `CreatePublicQueryAsync`; embeds Name/Phone/EventName into the Body (no schema change needed)
- **Decisions made**:
  - All existing event search methods require `_userContext.TenantId` — unusable for public; new methods are tenant-agnostic
  - `GetEventById` requires encrypted ID + tenant scope — slug-based lookup is a new method
  - `GetLeaderboardAsync` returns admin leaderboard format — not suitable; new `GetPublicResultsAsync` is paged/filterable
  - `SubmitQueryAsync` only accepts Subject/Body/SubmitterEmail; `CreatePublicQueryAsync` packs Name/Phone/EventName into the Body string since `SupportQuery` has no separate columns for them
  - `EventOrganizer` has no email field → `ContactEmail` in `PublicEventDetailDto` will remain null
  - EF Core filtered includes (`.Where()` inside `.Include()`) used for Races and Participants to honour soft-delete
  - `DataPagingList` / `DataResultsPagingList` type aliases in interface files prevent CS0104 ambiguity with same-named types in Models.Client

### 2026-04-23 — backend-agent — Racetik API tasks (API-1 through API-11)

- **What was built**: 9 API tasks from the Racetik feature spec
- **Files modified**:
  - `Runnatics.Models.Client/Requests/Events/EventSettings.cs` — removed `RemoveBanner`, `ShowResultSummaryForRaces`, `UseOldData`, `AllowParticipantEdit` from `EventSettingsRequest` (hardcoded server-side)
  - `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` — ignore the 4 removed EventSettings fields in mapper; ignore `BannerImage`/`BannerContentType` in `EventRequest → Event`; map `BannerImage → BannerBase64` in `Event → EventResponse`
  - `Runnatics.Services/EventsService.cs` — hardcode 4 fields to `false` in `CreateEventSettings`, `SaveEventAsync`, and `UpdateEventSettings`; add banner save on create; add banner existence check on update; update `GetPublicEventsAsync` to require `ConfirmedEvent = true` AND `Published = true`
  - `Runnatics.Models.Client/Requests/Events/EventRequest.cs` — added `BannerBase64` property
  - `Runnatics.Models.Client/Responses/Events/EventResponse.cs` — added `BannerBase64` property
  - `Runnatics.Models.Client/Responses/Participants/ParticipantSearchReponse.cs` — added `IsEpcMapped` (bool)
  - `Runnatics.Services/ParticipantImportService.cs` — set `IsEpcMapped` in `PopulateCheckpointTimesAsync`; handle `DateOfBirth` in `UpdateParticipantExtendedAsync`; add `ManualCheckpointTimes` handling (creates SplitTimes records, sets `IsManualTiming = true`); added `ExportParticipantsAsync` (xlsx via ClosedXML)
  - `Runnatics.Models.Client/Requests/Participant/UpdateParticipantRequest.cs` — added `DateOfBirth`, `ManualCheckpointTimes` (list of `ManualCheckpointTime`)
  - `Runnatics.Models.Data/Entities/Participant.cs` — added `IsManualTiming` (bool, default false)
  - `Runnatics.Services.Interface/IParticipantImportService.cs` — added `ExportParticipantsAsync`
  - `Runnatics.Api/Controller/ParticipantsController.cs` — added `GET ~/api/races/{raceId}/participants/export`
  - `Runnatics.Services.Interface/IBibMappingService.cs` — added `GetParticipantsWithMappingStatusAsync`
  - `Runnatics.Services/BibMappingService.cs` — implemented `GetParticipantsWithMappingStatusAsync` (paginated, search by name/BIB, filter by mapped/unmapped)
  - `Runnatics.Api/Controller/BibMappingsController.cs` — added `GET /api/bib-mappings/participants` endpoint
  - `Runnatics.Services/RfidReaderService.cs` — added 500ms RSSI debounce: buffers reads per EPC, broadcasts best RSSI per EPC after window expires
  - `Runnatics.Services/Runnatics.Services.csproj` — added ClosedXML 0.102.3
- **Files created**:
  - `Runnatics.Models.Client/Requests/BibMapping/GetEpcMappingRequest.cs` — pagination/filter request for EPC mapping
  - `Runnatics.Models.Client/Responses/BibMapping/BibMappingParticipantResponse.cs` — paginated participant+mapping response
  - `db/scripts/Participants_AddIsManualTiming_20260423.sql` — ALTER TABLE for new `IsManualTiming` column
- **Decisions made**:
  - API-2 (location fields optional): Already done — all fields are `string?` with no `[Required]` in `EventRequest`
  - API-5 (participant search): Already working — `ParticipantImportService.Search` correctly applies all filters simultaneously
  - API-10 (EPC uniqueness + override): Already implemented in `BibMappingService.CreateAsync` with full conflict handling
  - `BannerImage` on the `Event` entity is the base64 storage field — `BannerBase64` in DTOs maps to it
  - Manual checkpoint times use SplitTimes entity; `SplitTimeMs` is relative to earliest known reading, or 0 if unknown
  - RSSI debounce is per-EPC (not per-window) — multiple EPCs in the same window are each deduplicated independently
- **Pending**: Run `db/scripts/Participants_AddIsManualTiming_20260423.sql` against Azure SQL

### 2026-04-17 — backend-agent — Bug Fixes (testing-feedback)

- **Bug #7 (Critical) — Bulk upload data loss**: Root cause was operator precedence bug in `ParticipantImportService.ProcessStagingDataAsync` line 258-266. `raceId.HasValue ? ... : true && IsActive && !IsDeleted` skipped soft-delete filters when raceId was provided, causing deleted participants' bibs to be treated as duplicates. Fix: `(!raceId.HasValue || p.RaceId == raceId.Value)`. Also: invalid staging records now marked "Invalid" (not left as "Pending"), and ProcessImportResponse.Errors list now populated with per-row details.
- **Bug #12 — Race category change response empty**: `UpdateParticipantExtendedAsync` returned `Task` (void), controller returned `{ }`. Changed return type to `Task<ParticipantSearchReponse?>`, added `MapToSearchResponse` helper, controller now returns full participant data (Bib, Name, Gender, Phone, Email, AgeCategory, Status).
- **Bug #11 — Export endpoint missing**: No export endpoint existed. Created `GET /api/results/{eventId}/{raceId}/export` on ResultsController. Returns CSV with: BibNumber, Name, Email, Mobile, Gender, AgeCategory, Status, GunTime, ChipTime, OverallRank, GenderRank, CategoryRank, plus dynamic columns for each checkpoint's split time. Added Email/Phone to LeaderboardEntry DTO and AutoMapper mapping.
- **Bug #1 — Event edit past dates**: No past-date validation exists in code. ValidateEventRequest only checks for null. No fix needed — issue likely elsewhere (frontend or DB constraint).
- **Bug #4 — Location fields optional**: Fields (VenueName, City, Country) are already `string?` without [Required]. No fix needed.
- **Bug #10 — Checkpoint clone**: Endpoint exists at `POST {eventId}/{sourceRaceId}/{destinationRaceId}/clone`. Service logic looks correct. Issue likely frontend-side (routing/params).
- **Files modified**: ParticipantImportService.cs, IParticipantImportService.cs, ParticipantsController.cs, ResultsController.cs, AutoMapperMappingProfile.cs, LeaderboardEntry.cs

### 2026-05-02 — backend-agent — Manual Time Entry with Race Recalculation

- **What was built**: `POST /api/RFID/{eventId}/{raceId}/participant/{participantId}/manual-time` — records a manual finish time for a participant, then recalculates the full race ranking
- **Files created**:
  - `Runnatics.Models.Client/Requests/RFID/ManualTimeRequest.cs` — `{ FinishTimeMs: long }` body DTO
  - `Runnatics.Models.Client/Responses/RFID/ManualTimeResponse.cs` — returns updated rank, bib, formatted time, total finishers
- **Files modified**:
  - `Runnatics.Services.Interface/IResultsService.cs` — added `RecordManualTimeAsync(eventId, raceId, participantId, finishTimeMs)`
  - `Runnatics.Services/ResultsService.cs` — implemented `RecordManualTimeAsync`: upserts Results record (ManualFinishTimeMs + FinishTime/GunTime/NetTime), sets `Participant.IsManualTiming = true`, calls private `CalculateResultRankingsAsync` to re-rank ALL finishers in the race
  - `Runnatics.Api/Controller/RFIDController.cs` — injected `IResultsService`; added the POST endpoint
- **Decisions made**:
  - Upsert strategy: if a Results row exists (e.g., prior DNF), it is updated in-place; otherwise a new row is created — avoids wipeout of other participants' results
  - Only `CalculateResultRankingsAsync` is called (not the full `CalculateResultsAsync`), so existing RFID-derived finish times are preserved; rankings are simply recomputed across all Finished results
  - `Results.ManualFinishTimeMs` stores the raw admin entry; `FinishTime`/`GunTime`/`NetTime` are all set to the same value (no gun-to-chip offset available for manual entry)
  - `Participant.IsManualTiming = true` is set so the UI can distinguish chip vs. manual finishers

### 2026-05-05 — backend-agent — PublicController CLAUDE.md Compliance Fix

- **What was fixed**: Refactored `PublicController` to comply with all CLAUDE.md rules (Rule 2: thin controller only)
- **Violations removed**:
  - Entity `using` aliases (`Event`, `Results`) — controller no longer touches domain entities
  - All private mapping helpers (`MapToSummary`, `MapToDetail`, `MapToResultDto`, `GetBannerBase64`) — moved to service layer
  - In-memory year filter in `GetEvents` — moved to `GetPublicEventsAsync` as a DB-side filter
  - Multiple service calls per action (`GetEventResults`: 3 calls, `GetResultByBib`: 2 calls, `GetPublicStats`: 2 calls) — consolidated into single service calls
  - Business logic in controller (publish gate, DNF filter, bib match, stats arithmetic) — moved to service layer
- **Files modified**:
  - `Runnatics.Models.Client/Public/PublicStatsDto.cs` — new DTO for stats endpoint
  - `Runnatics.Services.Interface/IEventsService.cs` — `GetPublicEventsAsync` now returns `PublicPagedResultDto<PublicEventSummaryDto>` + `year` param; `GetPublicEventBySlugAsync` returns `PublicEventDetailDto?`; added `GetPublicStatsAsync`
  - `Runnatics.Services/EventsService.cs` — implemented updated signatures; added `MapToEventSummaryDto`, `MapToEventDetailDto`, `GetEventBannerBase64` private helpers; implemented `GetPublicStatsAsync`
  - `Runnatics.Services.Interface/IPublicResultsService.cs` — removed `GetPublicResultsAsync` and `GetEffectivePublicLeaderboardSettingsAsync` (now private); added `GetPublicEventResultsAsync` and `GetPublicResultByBibAsync`
  - `Runnatics.Services/PublicResultsService.cs` — `GetPublicResultsAsync` and `GetEffectivePublicLeaderboardSettingsAsync` made private; added `GetPublicEventResultsAsync`, `GetPublicResultByBibAsync`, `MapToResultDto` private static helper
  - `Runnatics.Api/Controller/PublicController.cs` — all actions now call exactly ONE service method; no entity types, no mapping, no business logic

### 2026-05-05 — backend-agent — SRP Refactoring of Public Results Changes

- **What was built**: Applied Single Responsibility Principle to the 2026-05-05 changes
- **Files modified**:
  - `Runnatics.Models.Client/Public/PublicGroupedLeaderboardDto.cs` — now contains only `PublicGroupedLeaderboardDto`
  - `Runnatics.Models.Client/Public/PublicParticipantDetailDto.cs` — now contains only `PublicParticipantDetailDto`
  - `Runnatics.Services.Interface/IResultsService.cs` — removed 4 public no-auth methods (`GetPublicResultsAsync`, `GetEffectivePublicLeaderboardSettingsAsync`, `GetPublicGroupedLeaderboardAsync`, `GetPublicParticipantDetailAsync`)
  - `Runnatics.Services/ResultsService.cs` — removed the same 4 methods; now admin-only
  - `Runnatics.Api/Controller/PublicController.cs` — now injects `IPublicResultsService` instead of `IResultsService`
  - `Runnatics.Api/Program.cs` — registered `IPublicResultsService → PublicResultsService`
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicGenderGroupDto.cs`
  - `Runnatics.Models.Client/Public/PublicCategoryGroupDto.cs`
  - `Runnatics.Models.Client/Public/PublicLeaderboardEntryDto.cs`
  - `Runnatics.Models.Client/Public/PublicParticipantInfoDto.cs`
  - `Runnatics.Models.Client/Public/PublicTimeDetailDto.cs`
  - `Runnatics.Models.Client/Public/PublicSplitDetailDto.cs`
  - `Runnatics.Services.Interface/IPublicResultsService.cs`
  - `Runnatics.Services/PublicResultsService.cs`
- **Decisions made**:
  - `IResultsService` is now admin-only; `IPublicResultsService` owns all anonymous public endpoints
  - `PublicResultsService` depends only on `IUnitOfWork`, `IEncryptionService`, `ILogger` — no `IMapper` or `IUserContextService` needed
  - One class per .cs file rule enforced across all 8 new DTO/service files

### 2026-05-05 — backend-agent — Excel Export Fix + Public Leaderboard + Public Participant Detail

- **What was built**: 3 features — fixed admin Excel export (Task 1), added public grouped leaderboard endpoint (Task 2), added public participant detail endpoint (Task 3)
- **Files modified**:
  - `Runnatics.Services/ResultsExportService.cs` — rewritten to bypass `GetLeaderboardAsync` entirely; now injects `RaceSyncDbContext` + `IEncryptionService` and queries Results directly (no leaderboard visibility gates). Builds 2-sheet Excel: "Overall Results" (all results with optional splits/pace columns from leaderboard settings) and "Category Results" (grouped by gender→category with merged group header rows)
  - `Runnatics.Services.Interface/IResultsService.cs` — added `GetPublicGroupedLeaderboardAsync` and `GetPublicParticipantDetailAsync`
  - `Runnatics.Services/ResultsService.cs` — implemented both new methods in `#region Public (no-auth) methods`
  - `Runnatics.Api/Controller/PublicController.cs` — added `GET api/public/{eventId}/{raceId}/leaderboard` and `GET api/public/participant/{participantId}`
- **Files created**:
  - `Runnatics.Models.Client/Public/PublicGroupedLeaderboardDto.cs` — 4 DTOs: `PublicGroupedLeaderboardDto`, `PublicGenderGroupDto`, `PublicCategoryGroupDto`, `PublicLeaderboardEntryDto`
  - `Runnatics.Models.Client/Public/PublicParticipantDetailDto.cs` — 4 DTOs: `PublicParticipantDetailDto`, `PublicParticipantInfoDto`, `PublicTimeDetailDto`, `PublicSplitDetailDto`
- **Decisions made**:
  - Excel export: bypasses leaderboard visibility pipeline entirely — admin always sees all results regardless of MaxDisplayedRecords/NumberOfResultsToShowOverall
  - Column control for export still honors leaderboard settings (ShowPace, ShowSplitTimes, ShowGenderResults, etc.)
  - Public grouped leaderboard: default shows top 3 per category (or `NumberOfResultsToShowCategory` from settings); `showAll=true` returns all
  - Participant detail URL format: `/p/{encryptedParticipantId}` built in service layer
  - `GetPublicGroupedLeaderboardAsync` accepts encrypted IDs (same as admin endpoints)
  - `GetPublicParticipantDetailAsync` accepts encrypted participantId
  - `ResultsExportService` no longer depends on `IResultsService` — removed that dependency, replaced with `RaceSyncDbContext` + `IEncryptionService`
