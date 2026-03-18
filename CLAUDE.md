# Runnatics.API — Claude Code Project Instructions

## Build & Run
```bash
dotnet build Runnatics.API.sln
dotnet run --project Runnatics/src/Runnatics.Api --urls "http://localhost:5286"
```

## Architecture
- **Style**: N-Layer (Domain → Application → Infrastructure → API)
- **ORM**: EF Core with `IEntityTypeConfiguration` — **NO migrations ever**
- **Database**: Azure SQL — schema via hand-written SQL scripts only
- **Auth**: JWT Bearer with multi-tenant claims (`tenantId`, `sub`, `role`)
- **Real-time**: SignalR — `RaceHub` at `/hubs/race`, `BibMappingHub` at `/hubs/bib-mapping`
- **Git**: branches follow `feature/{FeatureName}` from `master`

## Layer Map
| Layer | Project | Key classes |
|-------|---------|-------------|
| API | `Runnatics.Api` | Controllers, `Program.cs` |
| Application | `Runnatics.Services` | Services, `AutoMapperMappingProfile`, Hubs |
| Interfaces | `Runnatics.Services.Interface` | `ISimpleServiceBase`, service interfaces |
| Domain | `Runnatics.Models.Data` | Entities, `AuditProperties`, `PagingList<T>` |
| Client DTOs | `Runnatics.Models.Client` | Request/Response DTOs, `ResponseBase<T>` |
| EF Core | `Runnatics.Data.EF` | `RaceSyncDbContext`, `IEntityTypeConfiguration` |
| Repos | `Runnatics.Repositories.EF` | `GenericRepository<T>`, `UnitOfWork<C>` |

## Mandatory Patterns
- **AuditProperties** on every entity: `IsActive`, `IsDeleted`, `CreatedDate`, `CreatedBy`, `UpdatedDate`, `UpdatedBy`
- **IUnitOfWork** — never inject `IGenericRepository<T>` directly into services
- **IEncryptionService** — all public IDs in API responses must be AES-encrypted strings
- **IdEncryptor/IdDecryptor** — AutoMapper converters for int↔encrypted string mapping
- **ResponseBase<T>** — wraps all API responses
- **ServiceBase<T>** with `HasError`/`ErrorMessage` — no exception throwing to controllers
- **Soft delete only** — set `IsDeleted=true`, `IsActive=false`, never hard delete
- **AsNoTracking()** on all read-only queries
- **CancellationToken** on all async controller actions
- **Event timezone** — always convert UTC times to event's `TimeZone` field before returning

## Shared Context
All agents must READ `.claude/CONTEXT.md` before starting and WRITE to it after completing any task.

## Custom Agents
- `ef-core-agent` — entities, configurations, DbContext
- `backend-agent` — services, controllers, DTOs, AutoMapper
- `sql-agent` — SQL scripts, stored procedures

## Custom Commands
- `/new-feature` — scaffolds full feature stack across all layers
- `/review` — reviews code against Runnatics architecture standards
