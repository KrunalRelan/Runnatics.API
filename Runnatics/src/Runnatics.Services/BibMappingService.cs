using AutoMapper;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.BibMapping;
using Runnatics.Models.Client.Responses.BibMapping;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class BibMappingService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<BibMappingService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService,
        IValidator<CreateBibMappingRequest> validator) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IBibMappingService
    {
        private readonly IMapper _mapper = mapper;
        private readonly ILogger<BibMappingService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;
        private readonly IValidator<CreateBibMappingRequest> _validator = validator;

        public async Task<CreateBibMappingServiceResult> CreateAsync(CreateBibMappingRequest request, CancellationToken cancellationToken = default)
        {
            var result = new CreateBibMappingServiceResult();

            _logger.LogInformation(
                "CreateBibMapping request: BIB={Bib}, EPC={Epc}, Override={Override}, UserId={UserId}",
                request?.BibNumber, request?.Epc, request?.Override, _userContext.UserId);

            try
            {
                // Validate
                var validationResult = await _validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                {
                    ErrorMessage = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                    _logger.LogWarning("CreateBibMapping validation failed: {Error}", ErrorMessage);
                    return result;
                }

                var decryptedRaceId = int.Parse(_encryptionService.Decrypt(request.RaceId));
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var chipRepo = _repository.GetRepository<Chip>();
                var participantRepo = _repository.GetRepository<Participant>();
                var assignmentRepo = _repository.GetRepository<ChipAssignment>();
                var raceRepo = _repository.GetRepository<Race>();
                var eventRepo = _repository.GetRepository<Event>();

                // Look up the race to get the EventId
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId && !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (race == null)
                {
                    ErrorMessage = "Race not found.";
                    return result;
                }

                var eventId = race.EventId;

                // Resolve the event's display timezone for response times
                var eventTimeZoneId = await eventRepo
                    .GetQuery(e => e.Id == eventId)
                    .AsNoTracking()
                    .Select(e => e.TimeZone)
                    .FirstOrDefaultAsync(cancellationToken) ?? "Asia/Kolkata";

                TimeZoneInfo displayTz;
                try
                {
                    displayTz = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                    displayTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                }

                // Look up participant by BibNumber + RaceId
                var participant = await participantRepo
                    .GetQuery(p => p.BibNumber == request.BibNumber
                        && p.RaceId == decryptedRaceId
                        && !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (participant == null)
                {
                    ErrorMessage = $"No participant found with BIB '{request.BibNumber}' in this race.";
                    return result;
                }

                // Check if requested EPC is already assigned to some participant in this event
                var existingEpcAssignment = await assignmentRepo
                    .GetQuery(a => a.Chip.EPC == request.Epc
                        && a.EventId == eventId
                        && a.UnassignedAt == null
                        && !a.AuditProperties.IsDeleted, includeNavigationProperties: true)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                // Check if the target BIB/participant already has an active assignment
                var existingBibAssignment = await assignmentRepo
                    .GetQuery(a => a.ParticipantId == participant.Id
                        && a.EventId == eventId
                        && a.UnassignedAt == null
                        && !a.AuditProperties.IsDeleted, includeNavigationProperties: true)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                // Idempotent short-circuit: BIB already has exactly this EPC — return existing mapping as success
                if (existingBibAssignment != null
                    && existingEpcAssignment != null
                    && existingBibAssignment.ParticipantId == participant.Id
                    && existingEpcAssignment.ParticipantId == participant.Id
                    && existingBibAssignment.ChipId == existingEpcAssignment.ChipId)
                {
                    existingBibAssignment.Participant = participant;
                    result.Success = true;
                    result.Overridden = false;
                    result.SuccessMessage = $"BIB '{request.BibNumber}' is already mapped to EPC '{request.Epc}'.";
                    result.Mapping = _mapper.Map<BibMappingResponse>(existingBibAssignment, opts =>
                    {
                        opts.Items["DisplayTz"] = displayTz;
                        opts.Items["RaceId"] = request.RaceId;
                    });
                    return result;
                }

                // BIB already has a DIFFERENT EPC → conflict type #3 (unless override)
                if (existingBibAssignment != null && !request.Override)
                {
                    var currentEpc = existingBibAssignment.Chip?.EPC ?? string.Empty;
                    result.Conflict = new BibMappingConflictResponse
                    {
                        Success = false,
                        ConflictType = BibMappingConflictTypes.BibHasDifferentEpc,
                        Message = $"BIB #{request.BibNumber} already has EPC {currentEpc}. Replace it?",
                        BibNumber = request.BibNumber,
                        ExistingEpc = currentEpc
                    };
                    return result;
                }

                // EPC already mapped to a DIFFERENT participant → conflict type #1 (unless override)
                if (existingEpcAssignment != null
                    && existingEpcAssignment.ParticipantId != participant.Id
                    && !request.Override)
                {
                    Participant? otherParticipant = null;
                    if (existingEpcAssignment.Participant == null)
                    {
                        otherParticipant = await participantRepo
                            .GetQuery(p => p.Id == existingEpcAssignment.ParticipantId)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    else
                    {
                        otherParticipant = existingEpcAssignment.Participant;
                    }

                    result.Conflict = new BibMappingConflictResponse
                    {
                        Success = false,
                        ConflictType = BibMappingConflictTypes.EpcAlreadyMapped,
                        Message = "EPC already mapped",
                        ExistingMapping = new ExistingBibMappingInfo
                        {
                            BibNumber = otherParticipant?.BibNumber ?? string.Empty,
                            ParticipantName = otherParticipant == null
                                ? null
                                : $"{otherParticipant.FirstName} {otherParticipant.LastName}".Trim(),
                            ParticipantId = _encryptionService.Encrypt(existingEpcAssignment.ParticipantId.ToString()),
                            MappedAt = existingEpcAssignment.AssignedAt
                        }
                    };
                    return result;
                }

                // From here on, we will write. If override=true, we may need to unassign conflicting
                // rows before creating the new assignment. Wrap everything in a transaction.
                ChipAssignment? createdAssignment = null;
                Chip? chipForNew = null;
                string? overrideSuccessMessage = null;
                bool wasOverride = false;

                // Capture identifying info for logging before mutation
                string? displacedBib = null;
                string? replacedOldEpc = null;
                int? oldChipIdToFreeForBibOverride = null;

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    // Re-read tracked copies for mutation
                    if (existingEpcAssignment != null
                        && existingEpcAssignment.ParticipantId != participant.Id
                        && request.Override)
                    {
                        var trackedEpcAssignment = await assignmentRepo
                            .GetQuery(a => a.EventId == existingEpcAssignment.EventId
                                && a.ParticipantId == existingEpcAssignment.ParticipantId
                                && a.ChipId == existingEpcAssignment.ChipId
                                && a.UnassignedAt == null
                                && !a.AuditProperties.IsDeleted,
                                includeNavigationProperties: true)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (trackedEpcAssignment != null)
                        {
                            var displacedParticipant = await participantRepo
                                .GetQuery(p => p.Id == trackedEpcAssignment.ParticipantId)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(cancellationToken);
                            displacedBib = displacedParticipant?.BibNumber;

                            trackedEpcAssignment.UnassignedAt = DateTime.UtcNow;
                            trackedEpcAssignment.AuditProperties.IsDeleted = true;
                            trackedEpcAssignment.AuditProperties.IsActive = false;
                            trackedEpcAssignment.AuditProperties.UpdatedBy = userId;
                            trackedEpcAssignment.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            await assignmentRepo.UpdateAsync(trackedEpcAssignment);
                        }

                        wasOverride = true;
                    }

                    if (existingBibAssignment != null && request.Override)
                    {
                        var trackedBibAssignment = await assignmentRepo
                            .GetQuery(a => a.EventId == existingBibAssignment.EventId
                                && a.ParticipantId == existingBibAssignment.ParticipantId
                                && a.ChipId == existingBibAssignment.ChipId
                                && a.UnassignedAt == null
                                && !a.AuditProperties.IsDeleted,
                                includeNavigationProperties: true)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (trackedBibAssignment != null)
                        {
                            replacedOldEpc = trackedBibAssignment.Chip?.EPC;
                            oldChipIdToFreeForBibOverride = trackedBibAssignment.ChipId;

                            trackedBibAssignment.UnassignedAt = DateTime.UtcNow;
                            trackedBibAssignment.AuditProperties.IsDeleted = true;
                            trackedBibAssignment.AuditProperties.IsActive = false;
                            trackedBibAssignment.AuditProperties.UpdatedBy = userId;
                            trackedBibAssignment.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            await assignmentRepo.UpdateAsync(trackedBibAssignment);
                        }

                        wasOverride = true;
                    }

                    // Find or create Chip record by EPC (tracked)
                    var chip = await chipRepo
                        .GetQuery(c => c.EPC == request.Epc && !c.AuditProperties.IsDeleted)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (chip == null)
                    {
                        chip = new Chip
                        {
                            TenantId = tenantId,
                            EPC = request.Epc,
                            Status = "Assigned",
                            LastSeenAt = DateTime.UtcNow,
                            AuditProperties = new AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };
                        await chipRepo.AddAsync(chip);
                        // Flush inside the transaction so chip.Id is populated
                        // before ChipAssignment.ChipId references it (FK is part of the composite PK).
                        await _repository.SaveChangesAsync();
                    }
                    else
                    {
                        chip.Status = "Assigned";
                        chip.LastSeenAt = DateTime.UtcNow;
                        chip.AuditProperties.UpdatedBy = userId;
                        chip.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        await chipRepo.UpdateAsync(chip);
                    }

                    // If we freed up a different chip from the BIB's old assignment, mark it Available
                    if (oldChipIdToFreeForBibOverride.HasValue && oldChipIdToFreeForBibOverride.Value != chip.Id)
                    {
                        var freedChip = await chipRepo
                            .GetQuery(c => c.Id == oldChipIdToFreeForBibOverride.Value)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (freedChip != null)
                        {
                            freedChip.Status = "Available";
                            freedChip.AuditProperties.UpdatedBy = userId;
                            freedChip.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            await chipRepo.UpdateAsync(freedChip);
                        }
                    }

                    var assignment = new ChipAssignment
                    {
                        EventId = eventId,
                        ParticipantId = participant.Id,
                        ChipId = chip.Id,
                        AssignedAt = DateTime.UtcNow,
                        AssignedByUserId = userId,
                        AuditProperties = new AuditProperties
                        {
                            CreatedBy = userId,
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };
                    await assignmentRepo.AddAsync(assignment);

                    chipForNew = chip;
                    createdAssignment = assignment;
                });

                // Audit log for override actions
                if (wasOverride)
                {
                    if (!string.IsNullOrEmpty(displacedBib))
                    {
                        _logger.LogInformation(
                            "BIB mapping overridden (EPC reassigned): UserId={UserId}, OldBib={OldBib}, NewBib={NewBib}, Epc={Epc}, EventId={EventId}, Timestamp={Timestamp:o}",
                            userId, displacedBib, request.BibNumber, request.Epc, eventId, DateTime.UtcNow);
                        overrideSuccessMessage = $"EPC moved from BIB {displacedBib} to BIB {request.BibNumber}";
                    }
                    if (!string.IsNullOrEmpty(replacedOldEpc))
                    {
                        _logger.LogInformation(
                            "BIB mapping overridden (BIB EPC replaced): UserId={UserId}, Bib={Bib}, OldEpc={OldEpc}, NewEpc={NewEpc}, EventId={EventId}, Timestamp={Timestamp:o}",
                            userId, request.BibNumber, replacedOldEpc, request.Epc, eventId, DateTime.UtcNow);
                        overrideSuccessMessage = overrideSuccessMessage != null
                            ? $"{overrideSuccessMessage}; BIB {request.BibNumber}'s EPC changed from {replacedOldEpc} to {request.Epc}"
                            : $"BIB {request.BibNumber}'s EPC changed from {replacedOldEpc} to {request.Epc}";
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "BIB mapping created: EPC={Epc}, BIB={Bib}, ParticipantId={ParticipantId}, ChipId={ChipId}, EventId={EventId}",
                        request.Epc, request.BibNumber, participant.Id, chipForNew!.Id, eventId);
                }

                createdAssignment!.Chip = chipForNew!;
                createdAssignment.Participant = participant;

                result.Success = true;
                result.Overridden = wasOverride;
                result.SuccessMessage = overrideSuccessMessage ?? "BIB mapping created successfully.";
                result.Mapping = _mapper.Map<BibMappingResponse>(createdAssignment, opts =>
                {
                    opts.Items["DisplayTz"] = displayTz;
                    opts.Items["RaceId"] = request.RaceId;
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating BIB mapping for EPC={Epc}, BIB={Bib}", request.Epc, request.BibNumber);
                ErrorMessage = "Error creating BIB mapping.";
                return result;
            }
        }

        public async Task<List<BibMappingResponse>> GetByRaceAsync(string encryptedRaceId, CancellationToken cancellationToken = default)
        {
            try
            {
                var decryptedRaceId = int.Parse(_encryptionService.Decrypt(encryptedRaceId));

                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId && !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (race == null)
                {
                    ErrorMessage = "Race not found.";
                    return [];
                }

                // Resolve the event's display timezone
                var eventRepo = _repository.GetRepository<Event>();
                var eventTimeZoneId = await eventRepo
                    .GetQuery(e => e.Id == race.EventId)
                    .AsNoTracking()
                    .Select(e => e.TimeZone)
                    .FirstOrDefaultAsync(cancellationToken) ?? "Asia/Kolkata";

                TimeZoneInfo displayTz;
                try
                {
                    displayTz = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogWarning("Event {EventId} has unknown timezone '{TZ}', falling back to Asia/Kolkata", race.EventId, eventTimeZoneId);
                    displayTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                }

                var assignmentRepo = _repository.GetRepository<ChipAssignment>();
                var rawAssignments = await assignmentRepo
                    .GetQuery(
                        a => a.EventId == race.EventId
                            && a.Participant.RaceId == decryptedRaceId
                            && a.UnassignedAt == null
                            && !a.AuditProperties.IsDeleted,
                        includeNavigationProperties: true)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                return _mapper.Map<List<BibMappingResponse>>(rawAssignments, opts =>
                {
                    opts.Items["DisplayTz"] = displayTz;
                    opts.Items["RaceId"] = encryptedRaceId;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching BIB mappings for RaceId={RaceId}", encryptedRaceId);
                ErrorMessage = "Error fetching BIB mappings.";
                return [];
            }
        }

        public async Task<bool> DeleteAsync(string encryptedChipId, string encryptedParticipantId, string encryptedEventId, CancellationToken cancellationToken = default)
        {
            try
            {
                var chipId = int.Parse(_encryptionService.Decrypt(encryptedChipId));
                var participantId = int.Parse(_encryptionService.Decrypt(encryptedParticipantId));
                var eventId = int.Parse(_encryptionService.Decrypt(encryptedEventId));
                var userId = _userContext.UserId;

                var assignmentRepo = _repository.GetRepository<ChipAssignment>();
                var assignment = await assignmentRepo
                    .GetQuery(a => a.ChipId == chipId
                        && a.ParticipantId == participantId
                        && a.EventId == eventId
                        && a.UnassignedAt == null
                        && !a.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync(cancellationToken);

                if (assignment == null)
                {
                    ErrorMessage = "BIB mapping not found.";
                    return false;
                }

                // Soft-unassign: set UnassignedAt and soft-delete
                assignment.UnassignedAt = DateTime.UtcNow;
                assignment.AuditProperties.IsDeleted = true;
                assignment.AuditProperties.IsActive = false;
                assignment.AuditProperties.UpdatedBy = userId;
                assignment.AuditProperties.UpdatedDate = DateTime.UtcNow;

                await assignmentRepo.UpdateAsync(assignment);

                // Set chip back to Available
                var chipRepo = _repository.GetRepository<Chip>();
                var chip = await chipRepo.GetQuery(c => c.Id == chipId).FirstOrDefaultAsync(cancellationToken);
                if (chip != null)
                {
                    chip.Status = "Available";
                    chip.AuditProperties.UpdatedBy = userId;
                    chip.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await chipRepo.UpdateAsync(chip);
                }

                await _repository.SaveChangesAsync();

                _logger.LogInformation(
                    "BIB mapping deleted: ChipId={ChipId}, ParticipantId={ParticipantId}, EventId={EventId}",
                    chipId, participantId, eventId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting BIB mapping");
                ErrorMessage = "Error deleting BIB mapping.";
                return false;
            }
        }
    }
}
