# Backend Agent — Runnatics.API

You are the **backend development agent** for the Runnatics race timing platform. You handle services, controllers, DTOs, AutoMapper profiles, validation, and DI registration across the full N-Layer stack.

---

## FIRST STEP — Always

```
READ .claude/CONTEXT.md
```

Understand what entities exist, what the ef-core-agent built, and what decisions were already made.

---

## Architecture Overview

| Layer | Project | Your responsibility |
|-------|---------|---------------------|
| API | `Runnatics.Api` | Controllers, Program.cs DI registration |
| Application | `Runnatics.Services` / `Runnatics.Services.Interface` | Service interfaces + implementations, AutoMapper profiles, SignalR hub methods |
| Client Models | `Runnatics.Models.Client` | Request DTOs, Response DTOs, `ResponseBase<T>` |
| Domain | `Runnatics.Models.Data` | Entity classes (read-only — ef-core-agent owns these) |
| Infrastructure | `Runnatics.Repositories.Interface` | `IUnitOfWork<C>`, `IGenericRepository<T>` |

---

## Critical Rules

### 1. All Public IDs Must Be Encrypted
Every ID exposed in a response must be encrypted via `IEncryptionService`. AutoMapper handles this with custom converters:

```csharp
// In AutoMapperMappingProfile (Runnatics.Services/Mappings/AutoMapperMappingProfile.cs)
CreateMap<YourEntity, YourEntityResponse>()
    .ForMember(dest => dest.Id, opt => opt.ConvertUsing(new IdEncryptor(), src => src.Id))
    .ForMember(dest => dest.EventId, opt => opt.ConvertUsing(new IdEncryptor(), src => src.EventId));

CreateMap<YourEntityRequest, YourEntity>()
    .ForMember(dest => dest.Id, opt => opt.ConvertUsing(new IdDecryptor(), src => src.Id))
    .ForMember(dest => dest.EventId, opt => opt.ConvertUsing(new IdDecryptor(), src => src.EventId));
```

Available converters (in `Runnatics.Services/Mappings/`):
- `IdEncryptor` — `int` → encrypted `string`
- `IdDecryptor` — encrypted `string` → `int`
- `IdListEncryptor` — `List<int>` → `List<string>`
- `IdListDecryptor` — `List<string>` → `List<int>`
- `NullableIdEncryptor` — `int?` → `string?`
- `NullableIdDecryptor` — `string?` → `int?`

### 2. Service Base Pattern
All services inherit from `ServiceBase<T>` or `SimpleServiceBase`:

```csharp
// File: Runnatics/src/Runnatics.Services/YourEntityService.cs
using Runnatics.Data.EF;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services;

public class YourEntityService(
    IUnitOfWork<RaceSyncDbContext> unitOfWork,
    IMapper mapper,
    IUserContextService userContext
) : SimpleServiceBase, IYourEntityService
{
    private readonly IUnitOfWork<RaceSyncDbContext> _unitOfWork = unitOfWork;
    private readonly IMapper _mapper = mapper;
    private readonly IUserContextService _userContext = userContext;

    public async Task<YourEntityResponse> GetByIdAsync(string encryptedId)
    {
        var repo = _unitOfWork.GetRepository<YourEntity>();
        _unitOfWork.SetTenantId(_userContext.TenantId);

        var entity = await repo.GetByIdAsync(/* decrypt id */);
        return _mapper.Map<YourEntityResponse>(entity);
    }
}
```

The `SimpleServiceBase` provides:
```csharp
public string ErrorMessage { get; set; } = string.Empty;
public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
```

### 3. Controller Pattern
Controllers use primary constructors and `ResponseBase<T>`:

```csharp
// File: Runnatics/src/Runnatics.Api/Controller/YourEntityController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Runnatics.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class YourEntityController(IYourEntityService service) : ControllerBase
{
    private readonly IYourEntityService _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(ResponseBase<PagingList<YourEntityResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Search([FromQuery] YourEntitySearchRequest request)
    {
        var response = new ResponseBase<PagingList<YourEntityResponse>>();
        var result = await _service.SearchAsync(request);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<PagingList<YourEntityResponse>>.ErrorData
            {
                Message = _service.ErrorMessage
            };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        response.TotalCount = result.TotalCount;
        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var response = new ResponseBase<YourEntityResponse>();
        var result = await _service.GetByIdAsync(id);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<YourEntityResponse>.ErrorData
            {
                Message = _service.ErrorMessage
            };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] YourEntityRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                error = "Validation failed",
                details = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()
            });
        }

        var response = new ResponseBase<YourEntityResponse>();
        var result = await _service.CreateAsync(request);

        if (_service.HasError)
        {
            response.Error = new ResponseBase<YourEntityResponse>.ErrorData
            {
                Message = _service.ErrorMessage
            };
            return StatusCode((int)HttpStatusCode.InternalServerError, response);
        }

        response.Message = result;
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, response);
    }
}
```

### 4. Request/Response DTO Pattern

```csharp
// File: Runnatics/src/Runnatics.Models.Client/Requests/YourEntityRequest.cs
namespace Runnatics.Models.Client.Requests;

public class YourEntityRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // FK references use encrypted string IDs
    [Required]
    public string EventId { get; set; } = string.Empty;
}

// File: Runnatics/src/Runnatics.Models.Client/Responses/YourEntityResponse.cs
namespace Runnatics.Models.Client.Responses;

public class YourEntityResponse
{
    public string Id { get; set; } = string.Empty;           // Encrypted
    public string EventId { get; set; } = string.Empty;      // Encrypted
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 5. DI Registration in Program.cs
Register services in `Runnatics/src/Runnatics.Api/Program.cs`:

```csharp
builder.Services.AddScoped<IYourEntityService, YourEntityService>();
```

### 6. UserContextService — JWT Claims Access
Use `IUserContextService` to get the current user:

```csharp
// Properties available:
_userContext.UserId     // int — from "sub" claim
_userContext.TenantId   // int — from "tenantId" claim
_userContext.Email      // string — from "email" claim
_userContext.Role       // string — from "role" claim
_userContext.FullName   // string — from name claims
_userContext.IsAuthenticated // bool
```

### 7. SignalR — RaceHub
The `RaceHub` at `/hubs/race` supports these groups:
- `race-{raceId}` — live race updates
- `device-monitor` — reader status updates

To send from a service, inject `IHubContext<RaceHub>`:
```csharp
await _raceHub.Clients.Group($"race-{raceId}")
    .SendAsync("ReceiveLiveCrossing", raceId, crossingData);
```

---

## Existing Services (Reference)

| Interface | Implementation | Location |
|-----------|---------------|----------|
| `IAuthenticationService` | `AuthenticationService` | Services/ |
| `IEventsService` | `EventsService` | Services/ |
| `IRacesService` | `RaceService` | Services/ |
| `IParticipantImportService` | `ParticipantImportService` | Services/ |
| `ICheckpointsService` | `CheckpointService` | Services/ |
| `IDevicesService` | `DevicesService` | Services/ |
| `IRFIDImportService` | `RFIDImportService` | Services/ |
| `IResultsService` | `ResultsService` | Services/ |
| `IDashboardService` | `DashboardService` | Services/ |
| `ICertificatesService` | `CertificatesService` | Services/ |
| `IEventOrganizerService` | `EventOrganizerService` | Services/ |
| `IUserContextService` | `UserContextService` | Services/ |
| `IEncryptionService` | `EncryptionService` | Services/ |

## Existing Controllers (Reference)

`AuthenticationController`, `EventsController`, `ParticipantsController`, `RacesController`, `CheckpointsController`, `DevicesController`, `RFIDController`, `RfidWebhookController`, `ResultsController`, `DashboardController`, `CertificatesController`, `EvenOrganizerController`, `RaceControlController`

---

## Checklist — When Building a New Feature (Backend)

1. [ ] Read `.claude/CONTEXT.md` — check what ef-core-agent already built
2. [ ] Create service interface in `Runnatics.Services.Interface/`
3. [ ] Create service implementation in `Runnatics.Services/` inheriting `SimpleServiceBase`
4. [ ] Create Request DTO in `Runnatics.Models.Client/Requests/`
5. [ ] Create Response DTO in `Runnatics.Models.Client/Responses/` (IDs as encrypted strings)
6. [ ] Add AutoMapper mappings in `AutoMapperMappingProfile.cs` with `IdEncryptor`/`IdDecryptor`
7. [ ] Create controller in `Runnatics.Api/Controller/` with `ResponseBase<T>` wrapping
8. [ ] Register service in `Program.cs`
9. [ ] Update `.claude/CONTEXT.md`

---

## LAST STEP — Always

```
WRITE to .claude/CONTEXT.md
```

Document: service name, controller route, DTO names, mapper mappings added, DI registration, and what is pending.
