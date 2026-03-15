# /new-feature Command — Runnatics.API

Scaffolds a complete feature across all layers of the Runnatics N-Layer architecture.

---

## Usage

```
/new-feature <FeatureName>
```

Example: `/new-feature Sponsor` creates the full Sponsor feature stack.

---

## FIRST STEP

```
READ .claude/CONTEXT.md
```

---

## Workflow

### Step 1 — Git Branch
```bash
git checkout -b feature/<FeatureName>
```
Branch naming convention: `feature/{FeatureName}` from `master`.

### Step 2 — Domain Layer (ef-core-agent scope)

**Create Entity** in `Runnatics/src/Runnatics.Models.Data/Entities/<FeatureName>.cs`:
```csharp
namespace Runnatics.Models.Data.Entities;

public class <FeatureName>
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    // ... feature-specific properties ...
    public AuditProperties AuditProperties { get; set; } = new();
}
```

**Create Configuration** in `Runnatics/src/Runnatics.Data.EF/Config/<FeatureName>Configuration.cs`:
- Map to table `<FeatureName>s`
- Include full AuditProperties owned type mapping
- Add tenant and domain indexes

**Register in RaceSyncDbContext**:
- Add `DbSet<<FeatureName>> <FeatureName>s { get; set; }`
- Add `modelBuilder.ApplyConfiguration(new <FeatureName>Configuration())` in `OnModelCreating`

### Step 3 — Client Models (backend-agent scope)

**Create Request DTO** in `Runnatics/src/Runnatics.Models.Client/Requests/<FeatureName>Request.cs`:
```csharp
namespace Runnatics.Models.Client.Requests;

public class <FeatureName>Request
{
    [Required]
    public string Name { get; set; } = string.Empty;
    // FK IDs as encrypted strings
    public string EventId { get; set; } = string.Empty;
}
```

**Create Response DTO** in `Runnatics/src/Runnatics.Models.Client/Responses/<FeatureName>Response.cs`:
```csharp
namespace Runnatics.Models.Client.Responses;

public class <FeatureName>Response
{
    public string Id { get; set; } = string.Empty;  // Encrypted
    // All FK IDs as encrypted strings
}
```

### Step 4 — Service Layer (backend-agent scope)

**Create Interface** in `Runnatics/src/Runnatics.Services.Interface/I<FeatureName>Service.cs`:
```csharp
namespace Runnatics.Services.Interface;

public interface I<FeatureName>Service : ISimpleServiceBase
{
    Task<PagingList<<FeatureName>Response>> SearchAsync(<FeatureName>SearchRequest request);
    Task<<FeatureName>Response> GetByIdAsync(string encryptedId);
    Task<<FeatureName>Response> CreateAsync(<FeatureName>Request request);
    Task<<FeatureName>Response> UpdateAsync(string encryptedId, <FeatureName>Request request);
    Task<bool> DeleteAsync(string encryptedId);
}
```

**Create Implementation** in `Runnatics/src/Runnatics.Services/<FeatureName>Service.cs`:
- Inherit from `SimpleServiceBase`
- Inject `IUnitOfWork<RaceSyncDbContext>`, `IMapper`, `IUserContextService`
- Use `_unitOfWork.GetRepository<<FeatureName>>()` for data access
- Set `_unitOfWork.SetTenantId(_userContext.TenantId)` before queries

### Step 5 — AutoMapper Mappings

**Update** `Runnatics/src/Runnatics.Services/Mappings/AutoMapperMappingProfile.cs`:
```csharp
// Entity → Response (encrypt IDs)
CreateMap<<FeatureName>, <FeatureName>Response>()
    .ForMember(dest => dest.Id, opt => opt.ConvertUsing(new IdEncryptor(), src => src.Id));

// Request → Entity (decrypt IDs)
CreateMap<<FeatureName>Request, <FeatureName>>()
    .ForMember(dest => dest.EventId, opt => opt.ConvertUsing(new IdDecryptor(), src => src.EventId));
```

### Step 6 — Controller

**Create** `Runnatics/src/Runnatics.Api/Controller/<FeatureName>Controller.cs`:
- Route: `api/<feature-name>` (kebab-case)
- `[Authorize(Roles = "SuperAdmin,Admin")]`
- CRUD endpoints using `ResponseBase<T>` wrapper
- Check `_service.HasError` after every service call

### Step 7 — DI Registration

**Update** `Runnatics/src/Runnatics.Api/Program.cs`:
```csharp
builder.Services.AddScoped<I<FeatureName>Service, <FeatureName>Service>();
```

### Step 8 — SQL Script (sql-agent scope)

**Create** `SQL/<FeatureName>s_CreateTable.sql`:
- `CREATE TABLE` with all columns matching EF config
- Include all 6 AuditProperties columns
- `IF NOT EXISTS` guard
- Indexes for TenantId and unique constraints

---

## Files Created

After running `/new-feature Sponsor`, these files are created/modified:

| # | File | Action |
|---|------|--------|
| 1 | `Runnatics.Models.Data/Entities/Sponsor.cs` | Created |
| 2 | `Runnatics.Data.EF/Config/SponsorConfiguration.cs` | Created |
| 3 | `Runnatics.Data.EF/RaceSyncDbContext.cs` | Modified (DbSet + config) |
| 4 | `Runnatics.Models.Client/Requests/SponsorRequest.cs` | Created |
| 5 | `Runnatics.Models.Client/Responses/SponsorResponse.cs` | Created |
| 6 | `Runnatics.Services.Interface/ISponsorService.cs` | Created |
| 7 | `Runnatics.Services/SponsorService.cs` | Created |
| 8 | `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` | Modified |
| 9 | `Runnatics.Api/Controller/SponsorController.cs` | Created |
| 10 | `Runnatics.Api/Program.cs` | Modified (DI) |
| 11 | `SQL/Sponsors_CreateTable.sql` | Created |

---

## LAST STEP

```
WRITE to .claude/CONTEXT.md
```

Document all files created, the feature name, branch name, and any decisions made.
