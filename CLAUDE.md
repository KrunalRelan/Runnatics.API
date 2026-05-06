# Runnatics.API — Claude Code Project Instructions

## Build & Run
```bash
dotnet build Runnatics.API.sln
dotnet run --project Runnatics/src/Runnatics.Api --urls "http://localhost:5286"
```

## Architecture
- **Style**: N-Layer (Domain → Application → Infrastructure → API)
- **ORM**: EF Core with `IEntityTypeConfiguration` — **NO migrations EVER**
- **Schema**: Hand-written SQL scripts only — NEVER run `Add-Migration` or `Update-Database`
- **Relationships**: Fluent API in `IEntityTypeConfiguration` ONLY — NO DataAnnotations on entities
- **Query Style**: Lambda/method syntax ONLY — NO LINQ query syntax (`from x in y select x`)
- **UoW**: ALWAYS use `IUnitOfWork<RaceSyncDbContext>` — NEVER inject DbContext or IGenericRepository directly
- **Database**: Azure SQL
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
| EF Core | `Runnatics.Data.EF` | `RaceSyncDbContext`, `IEntityTypeConfiguration` configs |
| Repos | `Runnatics.Repositories.EF` | `GenericRepository<T>`, `UnitOfWork<C>` |

---

## MANDATORY RULES — Apply To Every Task

### RULE 1 — SOLID Principles
Before writing any class, verify:
- **S** — One reason to change (single responsibility)
- **O** — Extend behavior, never modify working code
- **L** — Derived classes substitutable for base
- **I** — Small focused interfaces, not fat ones
- **D** — Depend on abstractions (`IUnitOfWork`), not concretions (`DbContext`)

### RULE 2 — Controller = Thin Layer Only
Controllers do ONLY these things:
1. Receive HTTP request
2. Validate model state
3. Call ONE service method
4. Return result wrapped in `ResponseBase<T>`

**NEVER in controllers:** decrypt IDs, query database, map entities, write business logic, build response objects.

```csharp
// ✅ CORRECT
[HttpGet("{eventId}/{raceId}")]
public async Task<IActionResult> Get(string eventId, string raceId, CancellationToken ct)
{
    var response = new ResponseBase<ResultDto>();
    var result = await _service.GetAsync(eventId, raceId, ct);
    if (_service.HasError)
    {
        response.Error = new ResponseBase<ResultDto>.ErrorData { Message = _service.ErrorMessage };
        return NotFound(response);
    }
    response.Message = result;
    return Ok(response);
}

// ❌ WRONG
public async Task<IActionResult> Get(string eventId)
{
    var id = _encryption.Decrypt(eventId);          // ← move to service
    var items = await _context.Results               // ← move to service
        .Where(r => r.EventId == id).ToListAsync();
    return Ok(new { data = items, count = items.Count }); // ← move to service
}
```

### RULE 3 — Always Use IUnitOfWork
```csharp
// ✅ CORRECT
public class ResultService(IUnitOfWork<RaceSyncDbContext> unitOfWork, IMapper mapper)
    : SimpleServiceBase, IResultService
{
    public async Task<PagingList<ResultDto>?> GetAsync(string encryptedEventId, CancellationToken ct)
    {
        var eventId = /* decrypt here in service */;
        var query = unitOfWork.GetRepository<Result>()
            .GetQueryable()
            .AsNoTracking()
            .Where(r => r.EventId == eventId && r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(r => r.OverallRank)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);
        return new PagingList<ResultDto>(mapper.Map<List<ResultDto>>(items)) { TotalCount = total };
    }
}

// ❌ WRONG
public class ResultService(RunnaticsDbContext context) // ← NEVER inject DbContext
public class ResultService(IGenericRepository<Result> repo) // ← NEVER inject repo directly
```

### RULE 4 — Reuse Base Classes — Check Before Creating
```
Runnatics.Models.Client.Common.SearchCriteriaBase    ← ALL search requests inherit this
Runnatics.Models.Client.Common.SearchResponseBase<T> ← ALL search responses inherit this
Runnatics.Models.Client.Common.PagingList<T>         ← ALL paginated data uses this
Runnatics.Models.Client.Common.ResponseBase<T>       ← ALL API responses wrap in this
```

```csharp
// ✅ CORRECT — search request always inherits SearchCriteriaBase
// SearchCriteriaBase already has: PageNumber, PageSize, SearchString, SortFieldName, SortDirection
public class GetResultsRequest : SearchCriteriaBase
{
    public string? Gender { get; set; }
    public string? Category { get; set; }
    public string RankBy { get; set; } = "Overall";
}

// ❌ WRONG — duplicating base class fields
public class GetResultsRequest
{
    public int Page { get; set; }       // ← use PageNumber from SearchCriteriaBase
    public int Limit { get; set; }      // ← use PageSize from SearchCriteriaBase
    public string? Search { get; set; } // ← use SearchString from SearchCriteriaBase
}
```

### RULE 5 — EF Core: No Migrations, Fluent API Only, No DataAnnotations
```csharp
// ✅ CORRECT — entity (no annotations)
public class Result
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public string Status { get; set; } = string.Empty;
    public AuditProperties AuditProperties { get; set; } = new();
    public virtual Event Event { get; set; } = null!;
}

// ✅ CORRECT — configuration (Fluent API)
public class ResultConfiguration : IEntityTypeConfiguration<Result>
{
    public void Configure(EntityTypeBuilder<Result> builder)
    {
        builder.ToTable("Results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasMaxLength(50).IsRequired();
        builder.OwnsOne(r => r.AuditProperties, ap => { /* map audit columns */ });
        builder.HasOne(r => r.Event).WithMany(e => e.Results)
               .HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}

// ❌ WRONG — annotations on entity
public class Result
{
    [Required]       // ← NEVER
    [MaxLength(50)]  // ← NEVER
    public string Status { get; set; }
}
```

### RULE 6 — Lambda Syntax Only
```csharp
// ✅ CORRECT
var items = await query.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(ct);

// ❌ WRONG — LINQ query syntax
var items = from r in _context.Results where r.IsActive orderby r.Name select r;
```

### RULE 7 — REST Principles
- Nouns not verbs: `/api/results` not `/api/getResults`
- Plural: `/api/results` not `/api/result`
- Nested: `/api/events/{eventId}/races/{raceId}/results`
- GET=read, POST=create, PUT=full update, PATCH=partial, DELETE=soft delete

### RULE 8 — ID Encryption
- ALL public IDs in URLs and responses must be encrypted strings
- Decrypt using `IEncryptionService` in SERVICE layer, not controller
- Use `IdEncryptor`/`IdDecryptor` AutoMapper converters

### RULE 9 — Soft Delete Only
```csharp
entity.AuditProperties.IsDeleted = true;
entity.AuditProperties.IsActive = false;
entity.AuditProperties.UpdatedDate = DateTime.UtcNow;
entity.AuditProperties.UpdatedBy = currentUserId;
await unitOfWork.SaveChangesAsync(ct);
```

### RULE 10 — Query Best Practices
```csharp
.GetQueryable().AsNoTracking()                    // read-only queries
.Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted) // always filter
await query.AnyAsync(r => r.Bib == bib, ct)       // existence check, not Count > 0
public async Task<IActionResult> Get(CancellationToken ct) // always pass CancellationToken
```

### RULE 11 — Parameter Limit: 3 or More Parameters → POST + Request Class

**If a controller action needs 3 or more input parameters, NEVER pass them as individual query params. Instead:**
1. Create a request class inheriting `SearchCriteriaBase` (if paginated) or a plain request class
2. Use `[HttpPost]` with `[FromBody]` instead of `[HttpGet]` with `[FromQuery]`
3. Place the class in `Runnatics.Models.Client/Requests/{Feature}/`

```csharp
// ❌ WRONG — more than 2 query params
[HttpGet]
public async Task<IActionResult> Search(
    string? name,
    DateTime? dateFrom,
    DateTime? dateTo,
    EventStatus? status,
    int pageNumber = 1,
    int pageSize = 20,
    CancellationToken ct = default)

// ✅ CORRECT — request class + POST
[HttpPost("search")]
public async Task<IActionResult> Search(
    [FromBody] EventSearchRequest request,
    CancellationToken ct = default)
```

**Request class pattern — always inherit `SearchCriteriaBase` when paginated:**

```csharp
// File: Runnatics/src/Runnatics.Models.Client/Requests/Events/EventSearchRequest.cs
using Runnatics.Models.Client.Common;

namespace Runnatics.Models.Client.Requests.Events
{
    public class EventSearchRequest : SearchCriteriaBase
    {
        // Override defaults in constructor if needed
        public EventSearchRequest()
        {
            SortFieldName = "Id";
            SortDirection = SortDirection.Descending;
            // PageNumber = 1 and PageSize = 100 come from SearchCriteriaBase
        }

        /// <summary>Event name for partial match search (optional)</summary>
        public string? Name { get; set; }

        /// <summary>Start date for date range filter (optional)</summary>
        public DateTime? EventDateFrom { get; set; }

        /// <summary>End date for date range filter (optional)</summary>
        public DateTime? EventDateTo { get; set; }

        /// <summary>Event status filter (optional)</summary>
        public EventStatus? Status { get; set; }
    }
}
```

**`SearchCriteriaBase` already provides (never duplicate these):**
```
SearchString   — text search across relevant fields
SortFieldName  — column to sort by (default: "")
SortDirection  — Ascending or Descending (default: Ascending)
PageNumber     — current page (default: 1)
PageSize       — items per page (default: 100)
```

**Decision table:**

| Parameters | Has pagination? | Action |
|---|---|---|
| 1-2 params, no pagination | No | `[HttpGet]` with `[FromQuery]` is fine |
| 1-2 params, with pagination | Yes | Create class inheriting `SearchCriteriaBase`, use `[HttpPost("search")]` |
| 3+ params, any | Any | Create class (inherit `SearchCriteriaBase` if paginated), use `[HttpPost("search")]` |
| ID only | No | `[HttpGet("{id}")]` — single encrypted ID is always fine as route param |

### RULE 12 — Error Handling
- Use `GlobalExceptionMiddleware` — no try/catch in every service method
- Services inherit `SimpleServiceBase` — use `HasError`/`ErrorMessage`
- Never throw exceptions to controllers

---

## Shared Context
All agents MUST:
1. **READ** `.claude/CONTEXT.md` before starting
2. **WRITE** to `.claude/CONTEXT.md` after completing

## Custom Agents (`.claude/agents/`)
- `ef-core-agent` — entities, configurations, DbContext
- `backend-agent` — services, controllers, DTOs, AutoMapper
- `sql-agent` — SQL scripts, stored procedures
- `support-agent` — support ticket workflows

## Custom Commands (`.claude/commands/`)
- `/new-feature` — scaffolds full feature stack across all layers
- `/review` — reviews code against Runnatics architecture standards

## Rules (`.claude/rules/`)
Place granular rules here that apply to specific contexts.

## Skills (`.claude/skills/`)
Place reusable task patterns here.

---

## Pre-Task Checklist
- [ ] Read `.claude/CONTEXT.md`
- [ ] Does a similar service/method already exist? Search first
- [ ] Is this a search? → inherit `SearchCriteriaBase`
- [ ] Does response need pagination? → `PagingList<T>` + `SearchResponseBase<T>`
- [ ] Is this an API response? → wrap in `ResponseBase<T>`
- [ ] Are IDs encrypted? → decrypt in service, not controller
- [ ] Is business logic in controller? → move to service
- [ ] Filtering soft-deleted? → `!r.AuditProperties.IsDeleted && r.AuditProperties.IsActive`
- [ ] Read queries using `AsNoTracking()`?
- [ ] All async methods passing `CancellationToken`?
- [ ] Relationships in `IEntityTypeConfiguration`? No DataAnnotations?
- [ ] Lambda syntax everywhere? No LINQ query syntax?
