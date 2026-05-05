# Runnatics src/ — Targeted Architecture Reference

> Works together with root CLAUDE.md. Read BOTH before making any changes.

---

## Quick Reference — Where Does What Go?

| What | Where |
|------|-------|
| Business logic | `Runnatics.Services/{Feature}Service.cs` |
| Controller (thin) | `Runnatics.Api/Controller/{Feature}Controller.cs` |
| Service interface | `Runnatics.Services.Interface/I{Feature}Service.cs` |
| Search request DTO | `Runnatics.Models.Client/Requests/` inheriting `SearchCriteriaBase` |
| Response DTO | `Runnatics.Models.Client/Responses/` with encrypted string IDs |
| Entity (no annotations) | `Runnatics.Models.Data/Entities/` with `AuditProperties` |
| EF config (Fluent API) | `Runnatics.Data.EF/Config/{Entity}Configuration.cs` |
| AutoMapper mappings | `Runnatics.Services/Mappings/AutoMapperMappingProfile.cs` (existing file) |
| DI registration | `Runnatics.Api/Program.cs` |
| SQL scripts | `db/scripts/` |

---

## Reusable Base Classes — Always Use These

### SearchCriteriaBase
```csharp
// Runnatics.Models.Client.Common.SearchCriteriaBase
// ALWAYS inherit for search/list requests
public class SearchCriteriaBase
{
    public const int DefaultPageSize = 100;
    public string SearchString { get; set; } = string.Empty;
    public string SortFieldName { get; set; } = string.Empty;
    public SortDirection SortDirection { get; set; }    // Ascending default
    public int PageNumber { get; set; }                  // default 1
    public int PageSize { get; set; }                    // default 100
}

// ✅ Use like this:
public class GetParticipantsRequest : SearchCriteriaBase
{
    public string? Gender { get; set; }
    public string? Category { get; set; }
}
```

### SearchResponseBase<T>
```csharp
// Runnatics.Models.Client.Common.SearchResponseBase<T>
public abstract class SearchResponseBase<T> where T : class
{
    public string ErrorMessage { get; set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
}

// ✅ Use like this:
public class ParticipantSearchResponse : SearchResponseBase<ParticipantDto> { }
```

### PagingList<T>
```csharp
// Runnatics.Models.Data.PagingList<T>
// Use as return type for paginated service methods
return new PagingList<ResultDto>(mapper.Map<List<ResultDto>>(items))
{
    TotalCount = total
};
```

### ResponseBase<T>
```csharp
// Runnatics.Models.Client.Common.ResponseBase<T>
// ALL API responses wrap in this
var response = new ResponseBase<PagingList<ResultDto>>();
response.Message = result;
response.TotalCount = result.TotalCount;
return Ok(response);
```

---

## Standard Paginated Service Method

```csharp
public async Task<PagingList<YourDto>?> SearchAsync(
    YourSearchRequest request,
    CancellationToken ct)
{
    // 1. Decrypt IDs if needed
    // 2. Build base query
    var query = unitOfWork.GetRepository<YourEntity>()
        .GetQueryable()
        .AsNoTracking()
        .Where(e => e.TenantId == userContext.TenantId
                 && e.AuditProperties.IsActive
                 && !e.AuditProperties.IsDeleted);

    // 3. Apply search from SearchCriteriaBase.SearchString
    if (!string.IsNullOrEmpty(request.SearchString))
        query = query.Where(e => e.Name.Contains(request.SearchString));

    // 4. Apply additional filters
    if (!string.IsNullOrEmpty(request.Status))
        query = query.Where(e => e.Status == request.Status);

    // 5. Count before pagination
    var total = await query.CountAsync(ct);

    // 6. Sort using SearchCriteriaBase fields
    query = request.SortDirection == SortDirection.Descending
        ? query.OrderByDescending(e => e.Name)
        : query.OrderBy(e => e.Name);

    // 7. Paginate using SearchCriteriaBase fields
    var items = await query
        .Skip((request.PageNumber - 1) * request.PageSize)
        .Take(request.PageSize)
        .ToListAsync(ct);

    // 8. Map and return PagingList
    return new PagingList<YourDto>(mapper.Map<List<YourDto>>(items))
    {
        TotalCount = total
    };
}
```

---

## Standard Controller Action Patterns

### GET List
```csharp
[HttpGet]
public async Task<IActionResult> Search(
    [FromQuery] YourSearchRequest request,
    CancellationToken ct = default)
{
    var response = new ResponseBase<PagingList<YourDto>>();
    var result = await _service.SearchAsync(request, ct);

    if (_service.HasError)
    {
        response.Error = new() { Message = _service.ErrorMessage };
        return BadRequest(response);
    }

    response.Message = result;
    response.TotalCount = result?.TotalCount ?? 0;
    return Ok(response);
}
```

### GET Single
```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id, CancellationToken ct = default)
{
    var response = new ResponseBase<YourDto>();
    var result = await _service.GetByIdAsync(id, ct);

    if (_service.HasError)
    {
        response.Error = new() { Message = _service.ErrorMessage };
        return NotFound(response);
    }

    response.Message = result;
    return Ok(response);
}
```

### POST Create
```csharp
[HttpPost]
public async Task<IActionResult> Create(
    [FromBody] CreateYourEntityRequest request,
    CancellationToken ct = default)
{
    if (!ModelState.IsValid)
        return BadRequest(new { error = "Validation failed",
            details = ModelState.Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage).ToList() });

    var response = new ResponseBase<YourDto>();
    var result = await _service.CreateAsync(request, ct);

    if (_service.HasError)
    {
        response.Error = new() { Message = _service.ErrorMessage };
        return BadRequest(response);
    }

    response.Message = result;
    return CreatedAtAction(nameof(GetById), new { id = result?.Id }, response);
}
```

---

## Common Lambda Patterns

```csharp
// Paginated query (copy-paste template)
var query = unitOfWork.GetRepository<T>().GetQueryable().AsNoTracking()
    .Where(x => x.AuditProperties.IsActive && !x.AuditProperties.IsDeleted);
var total = await query.CountAsync(ct);
var items = await query.OrderBy(x => x.Id)
    .Skip((request.PageNumber - 1) * request.PageSize)
    .Take(request.PageSize)
    .ToListAsync(ct);
return new PagingList<TDto>(mapper.Map<List<TDto>>(items)) { TotalCount = total };

// Single item with navigation
var item = await unitOfWork.GetRepository<T>().GetQueryable().AsNoTracking()
    .Include(x => x.RelatedEntity)
    .FirstOrDefaultAsync(x => x.Id == id && !x.AuditProperties.IsDeleted, ct);

// Existence check — AnyAsync not Count
var exists = await unitOfWork.GetRepository<T>().GetQueryable()
    .AnyAsync(x => x.Name == name && !x.AuditProperties.IsDeleted, ct);

// Grouping
var grouped = items
    .GroupBy(r => r.Category)
    .Select(g => new CategoryDto
    {
        Category = g.Key,
        Items = g.OrderBy(r => r.Rank).ToList()
    })
    .ToList();

// Soft delete
entity.AuditProperties.IsDeleted = true;
entity.AuditProperties.IsActive = false;
entity.AuditProperties.UpdatedDate = DateTime.UtcNow;
entity.AuditProperties.UpdatedBy = userContext.UserId;
await unitOfWork.GetRepository<T>().UpdateAsync(entity, ct);
await unitOfWork.SaveChangesAsync(ct);
```

---

## New Feature Scaffold Checklist

- [ ] `Runnatics.Models.Data/Entities/{Entity}.cs` — no annotations, `AuditProperties`
- [ ] `Runnatics.Data.EF/Config/{Entity}Configuration.cs` — Fluent API, owns AuditProperties
- [ ] `Runnatics.Models.Client/Requests/Get{Entity}sRequest.cs` — inherits `SearchCriteriaBase`
- [ ] `Runnatics.Models.Client/Requests/Create{Entity}Request.cs`
- [ ] `Runnatics.Models.Client/Responses/{Entity}Dto.cs` — encrypted string IDs
- [ ] `Runnatics.Services.Interface/I{Entity}Service.cs`
- [ ] `Runnatics.Services/{Entity}Service.cs` — inherits `SimpleServiceBase`
- [ ] `Runnatics.Api/Controller/{Entity}sController.cs` — thin controller
- [ ] Add mappings to existing `AutoMapperMappingProfile.cs`
- [ ] Register in `Program.cs`
- [ ] Give SQL script to sql-agent
- [ ] Update `.claude/CONTEXT.md`
