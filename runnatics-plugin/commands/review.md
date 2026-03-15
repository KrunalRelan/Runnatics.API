# /review Command — Runnatics.API

Reviews code changes against the Runnatics architecture standards and common pitfalls.

---

## Usage

```
/review                    # Review all uncommitted changes
/review <file-path>        # Review a specific file
/review --staged           # Review only staged changes
```

---

## FIRST STEP

```
READ .claude/CONTEXT.md
```

Understand what feature is being built and what decisions were already made.

---

## Review Checklist

### 1. Entity Layer
- [ ] Entity has `AuditProperties AuditProperties { get; set; } = new();`
- [ ] Entity includes `TenantId` if tenant-scoped
- [ ] No EF migration files exist (hard fail)
- [ ] Namespace is `Runnatics.Models.Data.Entities`

### 2. EF Configuration
- [ ] Implements `IEntityTypeConfiguration<T>`
- [ ] `builder.ToTable("TableName")` — table name is plural
- [ ] `builder.HasKey(e => e.Id)` is set
- [ ] AuditProperties owned type is fully mapped with correct column names:
  - `CreatedDate` → `CreatedAt`, `UpdatedDate` → `UpdatedAt`
  - Defaults: `GETUTCDATE()` for CreatedAt, `true` for IsActive, `false` for IsDeleted
- [ ] Namespace is `Runnatics.Data.EF.Config`

### 3. DbContext Registration
- [ ] DbSet added to `RaceSyncDbContext`
- [ ] `ApplyConfiguration(new XConfiguration())` added to `OnModelCreating`

### 4. Service Layer
- [ ] Interface extends or aligns with `ISimpleServiceBase`
- [ ] Implementation inherits `SimpleServiceBase` or `ServiceBase<T>`
- [ ] Uses `IUnitOfWork<RaceSyncDbContext>` — not raw `DbContext`
- [ ] Calls `_unitOfWork.SetTenantId()` before repository queries
- [ ] Uses `_unitOfWork.GetRepository<T>()` — not injecting repos directly
- [ ] Sets `ErrorMessage` on failures (not throwing exceptions to controller)
- [ ] No direct `_context.SaveChanges()` — uses `_unitOfWork.SaveChangesAsync()`

### 5. DTOs
- [ ] Request DTOs in `Runnatics.Models.Client.Requests` namespace
- [ ] Response DTOs in `Runnatics.Models.Client.Responses` namespace
- [ ] All public IDs in Response DTOs are `string` (encrypted)
- [ ] FK IDs in Request DTOs are `string` (encrypted, decrypted via AutoMapper)
- [ ] Validation attributes present on request DTOs (`[Required]`, `[MaxLength]`, etc.)

### 6. AutoMapper
- [ ] Mapping registered in `AutoMapperMappingProfile`
- [ ] `IdEncryptor` used for Entity → Response ID fields
- [ ] `IdDecryptor` used for Request → Entity ID fields
- [ ] `NullableIdEncryptor`/`NullableIdDecryptor` for nullable FK IDs
- [ ] `IdListEncryptor`/`IdListDecryptor` for list ID fields

### 7. Controller
- [ ] Inherits `ControllerBase` (not `Controller`)
- [ ] Has `[ApiController]` and `[Route("api/[controller]")]`
- [ ] Has `[Authorize]` with appropriate roles
- [ ] Uses `ResponseBase<T>` for all responses
- [ ] Checks `_service.HasError` after every service call
- [ ] Uses `[ProducesResponseType]` attributes
- [ ] Validates `ModelState.IsValid` on POST/PUT endpoints
- [ ] Primary constructor pattern: `YourController(IYourService service) : ControllerBase`

### 8. DI Registration
- [ ] Service registered as `AddScoped<IXService, XService>()` in `Program.cs`
- [ ] Not registered as Singleton or Transient (services use scoped DbContext)

### 9. SQL Script (if present)
- [ ] Column names match IEntityTypeConfiguration exactly
- [ ] All 6 AuditProperties columns present with correct defaults
- [ ] `IF NOT EXISTS` guard on CREATE TABLE
- [ ] Appropriate indexes (TenantId, unique constraints)
- [ ] Azure SQL compatible (NVARCHAR, DATETIME2, BIT)

### 10. Security
- [ ] No raw SQL with string concatenation (SQL injection risk)
- [ ] No unencrypted IDs in API responses
- [ ] Authorization attributes on controllers/actions
- [ ] No secrets or connection strings in code

---

## Severity Levels

| Level | Meaning | Action |
|-------|---------|--------|
| **BLOCK** | Architecture violation, security issue, or data integrity risk | Must fix before merge |
| **WARN** | Deviation from convention, missing best practice | Should fix |
| **INFO** | Style suggestion, minor improvement | Optional |

---

## Output Format

```
## Review: <FeatureName>

### BLOCK
- [file:line] Description of issue

### WARN
- [file:line] Description of issue

### INFO
- [file:line] Description of suggestion

### Summary
X issues found (Y blocks, Z warnings, W info)
```

---

## LAST STEP

```
WRITE to .claude/CONTEXT.md
```

Document: review results, blocking issues found, and what needs to be fixed.
