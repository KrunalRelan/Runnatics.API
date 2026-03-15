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

        public async Task<BibMappingResponse?> CreateAsync(CreateBibMappingRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate
                var validationResult = await _validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                {
                    ErrorMessage = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                    return null;
                }

                var decryptedRaceId = int.Parse(_encryptionService.Decrypt(request.RaceId));
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var chipRepo = _repository.GetRepository<Chip>();
                var participantRepo = _repository.GetRepository<Participant>();
                var assignmentRepo = _repository.GetRepository<ChipAssignment>();

                // Look up the race to get the EventId
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId && !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (race == null)
                {
                    ErrorMessage = "Race not found.";
                    return null;
                }

                var eventId = race.EventId;

                // Check if EPC is already assigned to another participant in this event
                var existingEpcAssignment = await assignmentRepo
                    .GetQuery(a => a.Chip.EPC == request.Epc
                        && a.EventId == eventId
                        && a.UnassignedAt == null
                        && !a.AuditProperties.IsDeleted, includeNavigationProperties: true)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingEpcAssignment != null)
                {
                    ErrorMessage = $"EPC '{request.Epc}' is already mapped to another participant in this event.";
                    return null;
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
                    return null;
                }

                // Check if BIB is already mapped for this event
                var existingBibAssignment = await assignmentRepo
                    .GetQuery(a => a.ParticipantId == participant.Id
                        && a.EventId == eventId
                        && a.UnassignedAt == null
                        && !a.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingBibAssignment != null)
                {
                    ErrorMessage = $"BIB '{request.BibNumber}' is already mapped to a chip in this event.";
                    return null;
                }

                // Find or create Chip record by EPC
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
                    await _repository.SaveChangesAsync();
                }
                else
                {
                    // Update chip status
                    chip.Status = "Assigned";
                    chip.LastSeenAt = DateTime.UtcNow;
                    chip.AuditProperties.UpdatedBy = userId;
                    chip.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    await chipRepo.UpdateAsync(chip);
                    await _repository.SaveChangesAsync();
                }

                // Create ChipAssignment
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
                await _repository.SaveChangesAsync();

                _logger.LogInformation(
                    "BIB mapping created: EPC={Epc}, BIB={Bib}, ParticipantId={ParticipantId}, ChipId={ChipId}, EventId={EventId}",
                    request.Epc, request.BibNumber, participant.Id, chip.Id, eventId);

                return new BibMappingResponse
                {
                    ChipId = _encryptionService.Encrypt(chip.Id.ToString()),
                    ParticipantId = _encryptionService.Encrypt(participant.Id.ToString()),
                    RaceId = request.RaceId,
                    EventId = _encryptionService.Encrypt(eventId.ToString()),
                    BibNumber = request.BibNumber,
                    Epc = request.Epc,
                    ParticipantName = participant.FullName,
                    AssignedAt = assignment.AssignedAt,
                    CreatedAt = assignment.AuditProperties.CreatedDate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating BIB mapping for EPC={Epc}, BIB={Bib}", request.Epc, request.BibNumber);
                ErrorMessage = "Error creating BIB mapping.";
                return null;
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

                var assignmentRepo = _repository.GetRepository<ChipAssignment>();
                var assignments = await assignmentRepo
                    .GetQuery(
                        a => a.EventId == race.EventId
                            && a.Participant.RaceId == decryptedRaceId
                            && a.UnassignedAt == null
                            && !a.AuditProperties.IsDeleted,
                        includeNavigationProperties: true)
                    .AsNoTracking()
                    .Select(a => new BibMappingResponse
                    {
                        ChipId = _encryptionService.Encrypt(a.ChipId.ToString()),
                        ParticipantId = _encryptionService.Encrypt(a.ParticipantId.ToString()),
                        RaceId = encryptedRaceId,
                        EventId = _encryptionService.Encrypt(a.EventId.ToString()),
                        BibNumber = a.Participant.BibNumber ?? "",
                        Epc = a.Chip.EPC,
                        ParticipantName = a.Participant.FirstName + " " + a.Participant.LastName,
                        AssignedAt = a.AssignedAt,
                        CreatedAt = a.AuditProperties.CreatedDate
                    })
                    .ToListAsync(cancellationToken);

                return assignments;
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
