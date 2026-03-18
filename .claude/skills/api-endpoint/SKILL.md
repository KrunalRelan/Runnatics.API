# Skill: api-endpoint

Scaffolds a complete API endpoint across all layers: Controller action, Service method, DTOs, and AutoMapper mapping.

---

## Trigger

When user asks to add an API endpoint, route, or controller action.

---

## FIRST STEP

```
READ .claude/CONTEXT.md
```

---

## Inputs

| Parameter | Required | Description |
|-----------|----------|-------------|
| `EntityName` | Yes | Target entity (e.g., `Sponsor`) |
| `Action` | Yes | HTTP method + purpose (e.g., `GET search`, `POST create`, `PUT update`, `DELETE`) |
| `Route` | No | Custom route. Defaults to `api/{entity-name}` |
| `Roles` | No | Authorization roles. Defaults to `SuperAdmin,Admin` |
| `NeedsPagination` | No | Whether to return `PagingList<T>`. Defaults to `true` for search endpoints |

---

## Generation Steps

### 1. Request DTO (if POST/PUT)

File: `Runnatics/src/Runnatics.Models.Client/Requests/{EntityName}Request.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests;

public class {EntityName}Request
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // FK references as encrypted strings
    [Required]
    public string EventId { get; set; } = string.Empty;
}
```

For search endpoints, create:
```csharp
namespace Runnatics.Models.Client.Requests;

public class {EntityName}SearchRequest
{
    public int PageSize { get; set; } = 20;
    public int PageNumber { get; set; } = 1;
    public string? SearchTerm { get; set; }
    public string? SortField { get; set; }
    public string? SortDirection { get; set; }
}
```

### 2. Response DTO

File: `Runnatics/src/Runnatics.Models.Client/Responses/{EntityName}Response.cs`

```csharp
namespace Runnatics.Models.Client.Responses;

public class {EntityName}Response
{
    public string Id { get; set; } = string.Empty;           // Encrypted int
    public string EventId { get; set; } = string.Empty;      // Encrypted FK
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Rule**: Every `int` ID → `string` (encrypted). Every `int?` ID → `string?`.

### 3. AutoMapper Mapping

Add to `Runnatics/src/Runnatics.Services/Mappings/AutoMapperMappingProfile.cs`:

```csharp
// Entity → Response
CreateMap<{EntityName}, {EntityName}Response>()
    .ForMember(dest => dest.Id, opt => opt.ConvertUsing(new IdEncryptor(), src => src.Id))
    .ForMember(dest => dest.EventId, opt => opt.ConvertUsing(new IdEncryptor(), src => src.EventId))
    .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.AuditProperties.CreatedDate))
    .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.AuditProperties.IsActive));

// Request → Entity
CreateMap<{EntityName}Request, {EntityName}>()
    .ForMember(dest => dest.EventId, opt => opt.ConvertUsing(new IdDecryptor(), src => src.EventId));
```

### 4. Service Interface

File: `Runnatics/src/Runnatics.Services.Interface/I{EntityName}Service.cs`

```csharp
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;

namespace Runnatics.Services.Interface;

public interface I{EntityName}Service : ISimpleServiceBase
{
    Task<PagingList<{EntityName}Response>> SearchAsync({EntityName}SearchRequest request);
    Task<{EntityName}Response?> GetByIdAsync(string encryptedId);
    Task<{EntityName}Response?> CreateAsync({EntityName}Request request);
    Task<{EntityName}Response?> UpdateAsync(string encryptedId, {EntityName}Request request);
    Task<bool> DeleteAsync(string encryptedId);
}
```

### 5. Service Implementation

File: `Runnatics/src/Runnatics.Services/{EntityName}Service.cs`

```csharp
using AutoMapper;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services;

public class {EntityName}Service(
    IUnitOfWork<RaceSyncDbContext> unitOfWork,
    IMapper mapper,
    IUserContextService userContext,
    IEncryptionService encryptionService
) : SimpleServiceBase, I{EntityName}Service
{
    private readonly IUnitOfWork<RaceSyncDbContext> _unitOfWork = unitOfWork;
    private readonly IMapper _mapper = mapper;
    private readonly IUserContextService _userContext = userContext;
    private readonly IEncryptionService _encryptionService = encryptionService;

    public async Task<PagingList<{EntityName}Response>> SearchAsync({EntityName}SearchRequest request)
    {
        _unitOfWork.SetTenantId(_userContext.TenantId);
        var repo = _unitOfWork.GetRepository<{EntityName}>();

        var result = await repo.SearchAsync(
            filter: e => e.AuditProperties.IsDeleted == false,
            pageSize: request.PageSize,
            pageNumber: request.PageNumber,
            sortFieldName: request.SortField,
            sortDirection: request.SortDirection == "desc"
                ? SortDirection.Descending
                : SortDirection.Ascending
        );

        var mapped = _mapper.Map<PagingList<{EntityName}Response>>(result);
        mapped.TotalCount = result.TotalCount;
        return mapped;
    }

    public async Task<{EntityName}Response?> GetByIdAsync(string encryptedId)
    {
        _unitOfWork.SetTenantId(_userContext.TenantId);
        var repo = _unitOfWork.GetRepository<{EntityName}>();

        var id = int.Parse(_encryptionService.Decrypt(encryptedId));
        var entity = await repo.GetByIdAsync(id);

        if (entity == null)
        {
            ErrorMessage = "{EntityName} not found";
            return null;
        }

        return _mapper.Map<{EntityName}Response>(entity);
    }

    public async Task<{EntityName}Response?> CreateAsync({EntityName}Request request)
    {
        _unitOfWork.SetTenantId(_userContext.TenantId);
        var repo = _unitOfWork.GetRepository<{EntityName}>();

        var entity = _mapper.Map<{EntityName}>(request);
        entity.TenantId = _userContext.TenantId;
        entity.AuditProperties = new AuditProperties
        {
            CreatedDate = DateTime.UtcNow,
            CreatedBy = _userContext.UserId,
            IsActive = true,
            IsDeleted = false
        };

        var created = await repo.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<{EntityName}Response>(created);
    }

    public async Task<{EntityName}Response?> UpdateAsync(string encryptedId, {EntityName}Request request)
    {
        _unitOfWork.SetTenantId(_userContext.TenantId);
        var repo = _unitOfWork.GetRepository<{EntityName}>();

        var id = int.Parse(_encryptionService.Decrypt(encryptedId));
        var entity = await repo.GetByIdAsync(id);

        if (entity == null)
        {
            ErrorMessage = "{EntityName} not found";
            return null;
        }

        _mapper.Map(request, entity);
        entity.AuditProperties.UpdatedDate = DateTime.UtcNow;
        entity.AuditProperties.UpdatedBy = _userContext.UserId;

        var updated = await repo.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<{EntityName}Response>(updated);
    }

    public async Task<bool> DeleteAsync(string encryptedId)
    {
        _unitOfWork.SetTenantId(_userContext.TenantId);
        var repo = _unitOfWork.GetRepository<{EntityName}>();

        var id = int.Parse(_encryptionService.Decrypt(encryptedId));
        var entity = await repo.GetByIdAsync(id);

        if (entity == null)
        {
            ErrorMessage = "{EntityName} not found";
            return false;
        }

        // Soft delete
        entity.AuditProperties.IsDeleted = true;
        entity.AuditProperties.IsActive = false;
        entity.AuditProperties.UpdatedDate = DateTime.UtcNow;
        entity.AuditProperties.UpdatedBy = _userContext.UserId;

        await repo.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }
}
```

### 6. Controller Action

File: `Runnatics/src/Runnatics.Api/Controller/{EntityName}Controller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Requests;
using Runnatics.Models.Client.Responses;
using Runnatics.Models.Data.Common;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "{Roles}")]
public class {EntityName}Controller(I{EntityName}Service service) : ControllerBase
{
    private readonly I{EntityName}Service _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ResponseBase<PagingList<{EntityName}Response>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] {EntityName}SearchRequest request)
    {
        var response = new ResponseBase<PagingList<{EntityName}Response>>();
        var result = await _service.SearchAsync(request);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<PagingList<{EntityName}Response>>.ErrorData
            { Message = _service.ErrorMessage };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        response.TotalCount = result.TotalCount;
        return Ok(response);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ResponseBase<{EntityName}Response>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(string id)
    {
        var response = new ResponseBase<{EntityName}Response>();
        var result = await _service.GetByIdAsync(id);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<{EntityName}Response>.ErrorData
            { Message = _service.ErrorMessage };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResponseBase<{EntityName}Response>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] {EntityName}Request request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                error = "Validation failed",
                details = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage).ToList()
            });
        }

        var response = new ResponseBase<{EntityName}Response>();
        var result = await _service.CreateAsync(request);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<{EntityName}Response>.ErrorData
            { Message = _service.ErrorMessage };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, response);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ResponseBase<{EntityName}Response>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string id, [FromBody] {EntityName}Request request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                error = "Validation failed",
                details = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage).ToList()
            });
        }

        var response = new ResponseBase<{EntityName}Response>();
        var result = await _service.UpdateAsync(id, request);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<{EntityName}Response>.ErrorData
            { Message = _service.ErrorMessage };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string id)
    {
        await _service.DeleteAsync(id);

        if (_service.HasError)
        {
            return StatusCode((int)HttpStatusCode.InternalServerError,
                new { error = _service.ErrorMessage });
        }

        return NoContent();
    }
}
```

### 7. DI Registration

Add to `Runnatics/src/Runnatics.Api/Program.cs`:
```csharp
builder.Services.AddScoped<I{EntityName}Service, {EntityName}Service>();
```

---

## LAST STEP

```
WRITE to .claude/CONTEXT.md
```

Document: endpoint routes, DTO names, service registered, mapper mappings added.
