using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Races;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Linq.Expressions;
using System.Text;

namespace Runnatics.Services
{
    public class ParticipantImportService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<ParticipantImportService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IParticipantImportService
    {
        protected readonly IMapper _mapper = mapper;
        private readonly ILogger<ParticipantImportService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;

        public async Task<ParticipantImportResponse> UploadParticipantsCsvAsync(string eventId, ParticipantImportRequest request)
        {
            // Get user context
            var tenantId = _userContext.TenantId;
            var userId = _userContext.UserId;

            // Decrypt event ID
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));

            // Get race ID from request
            int? raceId = request.RaceId != null ? Convert.ToInt32(_encryptionService.Decrypt(request.RaceId)) : (int?)null;

            var response = new ParticipantImportResponse
            {
                FileName = request.File.FileName,
                UploadedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            try
            {
                _logger.LogInformation("Starting CSV upload for Event {EventId}, TenantId {TenantId}", decryptedEventId, tenantId);

                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                if (!request.File.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = "Only CSV files are supported";
                    _logger.LogWarning("Upload failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Validate event exists and belongs to tenant
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.TenantId == tenantId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Upload failed: Event {EventId} not found for Tenant {TenantId}", decryptedEventId, tenantId);
                    response.Status = "Failed";
                    return response;
                }

                // Parse CSV file
                var stagingRecords = await ParseCsvFileAsync(request.File);

                if (stagingRecords.Count == 0)
                {
                    ErrorMessage = "No valid records found in CSV file";
                    _logger.LogWarning("Upload failed: No valid records in CSV");
                    response.Status = "Failed";
                    return response;
                }

                // Create import batch
                var importBatch = new ImportBatch
                {
                    TenantId = tenantId,
                    EventId = decryptedEventId,
                    FileName = request.File.FileName,
                    TotalRecords = stagingRecords.Count,
                    // SuccessCount = 0,
                    // ErrorCount = 0,
                    Status = "Pending",
                    //UploadedAt = DateTime.UtcNow,
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    }
                };

                var batchRepo = _repository.GetRepository<ImportBatch>();
                await batchRepo.AddAsync(importBatch);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Created ImportBatch {BatchId} with {Count} records", importBatch.Id, stagingRecords.Count);

                // Validate and insert staging records
                var validRecords = 0;
                var invalidRecords = 0;
                var stagingRepo = _repository.GetRepository<ParticipantStaging>();

                foreach (var record in stagingRecords)
                {
                    record.ImportBatchId = importBatch.Id;
                    record.AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    };

                    // Basic validation
                    var validationErrors = ValidateRecord(record);
                    if (validationErrors.Any())
                    {
                        invalidRecords++;
                        foreach (var error in validationErrors)
                        {
                            response.Errors.Add(error);
                        }
                    }
                    else
                    {
                        validRecords++;
                    }

                    await stagingRepo.AddAsync(record);
                }

                await _repository.SaveChangesAsync();

                response.ImportBatchId = _encryptionService.Encrypt(Convert.ToString(importBatch.Id));
                response.TotalRecords = stagingRecords.Count;
                response.ValidRecords = validRecords;
                response.InvalidRecords = invalidRecords;
                response.Status = invalidRecords > 0 ? "PartiallyValidated" : "Validated";

                _logger.LogInformation("CSV upload completed. Valid: {Valid}, Invalid: {Invalid}", validRecords, invalidRecords);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading CSV: {ex.Message}";
                _logger.LogError(ex, "Error uploading CSV file");
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<ProcessImportResponse> ProcessStagingDataAsync(ProcessImportRequest request)
        {
            // Get user context
            var tenantId = _userContext.TenantId;
            var userId = _userContext.UserId;

            // Decrypt IDs
            var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(request.EventId));
            var decryptedImportBatchId = Convert.ToInt32(_encryptionService.Decrypt(request.ImportBatchId));
            var raceId = request.RaceId != null ? Convert.ToInt32(_encryptionService.Decrypt(request.RaceId)) : (int?)null;

            var response = new ProcessImportResponse
            {
                ImportBatchId = decryptedImportBatchId,
                ProcessedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            try
            {
                _logger.LogInformation("Starting processing for ImportBatch {BatchId}", decryptedImportBatchId);

                // Get import batch
                var batchRepo = _repository.GetRepository<ImportBatch>();
                var importBatch = await batchRepo.GetQuery(b =>
                    b.Id == decryptedImportBatchId &&
                    b.TenantId == tenantId &&
                    b.EventId == decryptedEventId)
                    .FirstOrDefaultAsync();

                if (importBatch == null)
                {
                    ErrorMessage = "Import batch not found";
                    _logger.LogWarning("Import batch {BatchId} not found", decryptedImportBatchId);
                    response.Status = "Failed";
                    return response;
                }

                // Validate race if provided
                if (raceId.HasValue)
                {
                    var raceRepo = _repository.GetRepository<Race>();
                    var raceExists = await raceRepo.GetQuery(r =>
                        r.Id == raceId.Value &&
                        r.EventId == decryptedEventId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .AnyAsync();

                    if (!raceExists)
                    {
                        ErrorMessage = "Race not found";
                        _logger.LogWarning("Race {RaceId} not found for Event {EventId}", raceId, decryptedEventId);
                        response.Status = "Failed";
                        return response;
                    }
                }

                // Get pending staging records
                var stagingRepo = _repository.GetRepository<ParticipantStaging>();
                var stagingRecords = await stagingRepo.GetQuery(s =>
                    s.ImportBatchId == decryptedImportBatchId &&
                    s.ProcessingStatus == "Pending")
                    .ToListAsync();

                if (stagingRecords.Count == 0)
                {
                    ErrorMessage = "No pending records to process";
                    _logger.LogWarning("No pending records for ImportBatch {BatchId}", decryptedImportBatchId);
                    response.Status = "Completed";
                    return response;
                }

                var successCount = 0;
                var errorCount = 0;
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var batchProcessor = await _repository.ExecuteStoredProcedure<ParticipantsStagingRequest, ProcessImportResponse>("sp_ProcessParticipantStaging",

                   new ParticipantsStagingRequest
                   {
                       ImportBatchId = decryptedImportBatchId,
                       TenantId = tenantId,
                       EventId = decryptedEventId,
                       RaceId = raceId ?? 0,
                       UserId = userId
                   }, "");


                _logger.LogInformation("Processing completed. Success: {Success}, Errors: {Errors}", successCount, errorCount);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error processing import: {ex.Message}";
                _logger.LogError(ex, "Error processing import batch {BatchId}", decryptedImportBatchId);
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<PagingList<ParticipantSearchReponse>> Search(ParticipantSearchRequest request, string eventId, string raceId)
        {
            try
            {

                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                // Validate and sanitize pagination parameters
                var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
                var pageSize = request.PageSize > 0 && request.PageSize <= 1000
                    ? request.PageSize
                    : SearchCriteriaBase.DefaultPageSize;

                _logger.LogInformation(
                    "Searching participants for EventId: {EventId}, RaceId: {RaceId}, PageNumber: {PageNumber}, PageSize: {PageSize}",
                    decryptedEventId,
                    decryptedRaceId,
                    pageNumber,
                    pageSize
                );

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var baseExpression = BuildSearchExpression(request, decryptedEventId, decryptedRaceId);

                // If a free-text search string is provided, extend the expression to include ORed fields
                if (!string.IsNullOrWhiteSpace(request.SearchString))
                {
                    baseExpression = CombineExpressionWithSearch(baseExpression, request.SearchString.Trim());
                }

                var mappedSortField = GetMappedSortField(request.SortFieldName);

                var response = await participantRepo.SearchAsync(
                    baseExpression,
                    pageSize,              // Use validated pageSize
                    pageNumber,            // Use validated pageNumber
                    request.SortDirection == SortDirection.Ascending
                        ? Models.Data.Common.SortDirection.Ascending
                        : Models.Data.Common.SortDirection.Descending,
                    mappedSortField,
                    false,
                    false
                );

                _logger.LogInformation(
                    "Found {TotalCount} participants, returning page {PageNumber} with items",
                    response.TotalCount,
                    pageNumber);

                var toReturn = _mapper.Map<PagingList<ParticipantSearchReponse>>(response);

                // Ensure TotalCount is preserved
                toReturn.TotalCount = response.TotalCount;

                return toReturn;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching participants: {ex.Message}";
                _logger.LogError(
                    ex,
                    "Error fetching participants for event {EventId} and race {RaceId}. Request: {@Request}",
                    eventId,
                    raceId,
                    request
                );

                // Return an empty paging list on error
                return new PagingList<ParticipantSearchReponse>();
            }
        }

        public async Task AddParticipant(string eventId, string raceId, ParticipantRequest addParticipant)
        {
            try
            {
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var eventIdInt = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var raceIdInt = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var participant = _mapper.Map<Models.Data.Entities.Participant>(addParticipant);

                participant.EventId = eventIdInt;
                participant.RaceId = raceIdInt;
                participant.TenantId = _userContext.TenantId;

                // Duplicate bib check: ensure no active participant exists with same bib in same tenant/event/race
                if (!string.IsNullOrWhiteSpace(participant.BibNumber))
                {
                    var exists = await participantRepo.GetQuery(p =>
                        p.TenantId == participant.TenantId &&
                        p.EventId == participant.EventId &&
                        p.RaceId == participant.RaceId &&
                        p.BibNumber == participant.BibNumber &&
                        p.AuditProperties.IsActive &&
                        !p.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .AnyAsync();

                    if (exists)
                    {
                        ErrorMessage = "Participant with the same BIB number already exists for this race.";
                        _logger.LogWarning("AddParticipant aborted - duplicate bib {Bib} for Event {EventId} Race {RaceId} Tenant {TenantId}", participant.BibNumber, eventIdInt, raceIdInt, participant.TenantId);
                        return;
                    }
                }

                participant.AuditProperties = new Models.Data.Common.AuditProperties
                {
                    CreatedBy = _userContext.UserId,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true,
                    IsDeleted = false
                };
                await _repository.BeginTransactionAsync();

                var entity = await participantRepo.AddAsync(participant);

                await _repository.SaveChangesAsync();

                await _repository.CommitTransactionAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error inserting participants: {ex.Message}";
                _logger.LogError(ex, "Error while inserting the participant {bibnumber}", addParticipant.BibNumber);
                try
                {
                    await _repository.RollbackTransactionAsync();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed or there was no active transaction to rollback.");
                    this.ErrorMessage = "Rollback failed.";
                }
            }
        }

        public async Task EditParticipant(string participantId, ParticipantRequest editParticipant)
        {
            try
            {
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var decryptParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(editParticipant.RaceId));

                var existingParticipant = await participantRepo.GetQuery(p => p.Id == decryptParticipantId
                                                                              && p.AuditProperties.IsActive
                                                                              && !p.AuditProperties.IsDeleted)
                                                               .FirstOrDefaultAsync();
                if (existingParticipant == null)
                {
                    ErrorMessage = "Participant not found";
                    _logger.LogWarning("Edit failed: Participant {ParticipantId} not found", participantId);
                    return;
                }

                _mapper.Map(editParticipant, existingParticipant);

                ///If existing race id is different than the new race id, then user is moving participant to another race.
                ///so, we need to deleted that record from existing and add a new record in the new race.
                if (existingParticipant.RaceId != decryptedRaceId)
                {
                    //insert the record in participant table with new race id
                    var newParticipant = _mapper.Map<Models.Data.Entities.Participant>(editParticipant);
                    newParticipant.EventId = existingParticipant.EventId;
                    newParticipant.TenantId = _userContext.TenantId;
                    newParticipant.RaceId = decryptedRaceId;
                    newParticipant.AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        CreatedBy = _userContext.UserId,
                        CreatedDate = DateTime.UtcNow,
                        IsActive = true,
                        IsDeleted = false
                    };
                    await _repository.BeginTransactionAsync();

                    await participantRepo.AddAsync(newParticipant);
                    //delete the existing record
                    existingParticipant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    existingParticipant.AuditProperties.IsActive = false;
                    existingParticipant.AuditProperties.IsDeleted = true;
                    existingParticipant.AuditProperties.UpdatedBy = _userContext.UserId;

                    await participantRepo.UpdateAsync(existingParticipant);
                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();
                    return;
                }
                else
                {
                    existingParticipant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    existingParticipant.AuditProperties.IsActive = true;
                    existingParticipant.AuditProperties.IsDeleted = false;
                }

                await _repository.BeginTransactionAsync();

                var entity = await participantRepo.UpdateAsync(existingParticipant);

                await _repository.SaveChangesAsync();

                await _repository.CommitTransactionAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating participants: {ex.Message}";
                _logger.LogError(ex, "Error while updating the participant {participantId}", participantId);
                try
                {
                    await _repository.RollbackTransactionAsync();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed or there was no active transaction to rollback.");
                    this.ErrorMessage = "Rollback failed.";
                }
            }
        }

        public async Task DeleteParicipant(string participantId)
        {
            try
            {
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var decryptParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                var existingParticipant = await participantRepo.GetQuery(p => p.Id == decryptParticipantId
                                                                              && p.AuditProperties.IsActive
                                                                              && !p.AuditProperties.IsDeleted)
                                                               .FirstOrDefaultAsync();
                if (existingParticipant == null)
                {
                    ErrorMessage = "Participant not found";
                    _logger.LogWarning("Edit failed: Participant {ParticipantId} not found", participantId);
                    return;
                }

                existingParticipant.AuditProperties = new Models.Data.Common.AuditProperties
                {
                    UpdatedBy = _userContext.UserId,
                    UpdatedDate = DateTime.UtcNow,
                    IsActive = false,
                    IsDeleted = true
                };

                await _repository.BeginTransactionAsync();

                var entity = await participantRepo.UpdateAsync(existingParticipant);

                await _repository.SaveChangesAsync();

                await _repository.CommitTransactionAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error delete participants: {ex.Message}";
                _logger.LogError(ex, "Error while deleting the participant {participantId}", participantId);
                try
                {
                    await _repository.RollbackTransactionAsync();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed or there was no active transaction to rollback.");
                    this.ErrorMessage = "Rollback failed.";
                }
            }
        }

        public async Task<List<Category>> GetCategories(string eventId, string raceId)
        {
            try
            {
                var decryptedEventId = _encryptionService.Decrypt(eventId);
                var decryptedRaceId = _encryptionService.Decrypt(raceId);

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var categories = participantRepo.GetQuery(p => p.EventId.ToString() == decryptedEventId
                                                              && p.RaceId.ToString() == decryptedRaceId
                                                              && p.AuditProperties.IsActive
                                                              && !p.AuditProperties.IsDeleted);

                var toReturn = categories.Select(s =>
                                                    new Category
                                                    {
                                                        CategoryName = s.AgeCategory ?? string.Empty,
                                                    }).Distinct()
                                                      .ToList();

                return await Task.FromResult(toReturn);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error fetching categories: {ex.Message}";
                _logger.LogError(ex, "Error while fetching categories for event {eventId} and race {raceId}", eventId, raceId);
                return [];
            }
        }

        public async Task<AddParticipantRangeResponse> AddParticipantRangeAsync(string eventId, string raceId, AddParticipantRangeRequest request)
        {
            var response = new AddParticipantRangeResponse
            {
                Status = "Processing"
            };

            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                _logger.LogInformation(
                    "Adding participant range for EventId: {EventId}, RaceId: {RaceId}, From: {From}, To: {To}, Prefix: {Prefix}, Suffix: {Suffix}",
                    decryptedEventId, decryptedRaceId, request.FromBibNumber, request.ToBibNumber,
                    request.Prefix ?? "none", request.Suffix ?? "none");

                // Validate event exists and belongs to tenant
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.TenantId == tenantId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Add range failed: Event {EventId} not found for Tenant {TenantId}",
                        decryptedEventId, tenantId);
                    response.Status = "Failed";
                    return response;
                }

                // Validate race exists
                var raceRepo = _repository.GetRepository<Race>();
                var raceExists = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!raceExists)
                {
                    ErrorMessage = "Race not found";
                    _logger.LogWarning("Add range failed: Race {RaceId} not found for Event {EventId}",
                        decryptedRaceId, decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                // Get existing bib numbers for this event/race to avoid duplicates
                var existingBibs = await participantRepo.GetQuery(p =>
                    p.EventId == decryptedEventId &&
                    p.RaceId == decryptedRaceId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .Select(p => p.BibNumber)
                    .ToListAsync();

                var existingBibSet = new HashSet<string>(existingBibs, StringComparer.OrdinalIgnoreCase);

                var participantsToAdd = new List<Models.Data.Entities.Participant>();
                var skippedBibs = new List<string>();

                // Generate bib numbers in the range
                for (int i = request.FromBibNumber; i <= request.ToBibNumber; i++)
                {
                    // Build bib number with optional prefix and suffix
                    var bibNumber = BuildBibNumber(request.Prefix, i, request.Suffix);

                    // Check for duplicates
                    if (existingBibSet.Contains(bibNumber))
                    {
                        skippedBibs.Add(bibNumber);
                        continue;
                    }

                    var participant = new Models.Data.Entities.Participant
                    {
                        TenantId = tenantId,
                        EventId = decryptedEventId,
                        RaceId = decryptedRaceId,
                        BibNumber = bibNumber,
                        AuditProperties = new Models.Data.Common.AuditProperties
                        {
                            CreatedBy = userId,
                            CreatedDate = DateTime.UtcNow,
                            IsActive = true,
                            IsDeleted = false
                        }
                    };

                    participantsToAdd.Add(participant);
                    existingBibSet.Add(bibNumber); // Add to set to catch duplicates within the range
                }

                if (participantsToAdd.Count > 0)
                {
                    await _repository.BeginTransactionAsync();

                    try
                    {
                        // Bulk insert participants
                        foreach (var participant in participantsToAdd)
                        {
                            await participantRepo.AddAsync(participant);
                        }

                        await _repository.SaveChangesAsync();
                        await _repository.CommitTransactionAsync();

                        _logger.LogInformation(
                            "Successfully added {Count} participants for EventId: {EventId}, RaceId: {RaceId}",
                            participantsToAdd.Count, decryptedEventId, decryptedRaceId);
                    }
                    catch (Exception ex)
                    {
                        await _repository.RollbackTransactionAsync();
                        throw;
                    }
                }

                response.TotalCreated = participantsToAdd.Count;
                response.TotalSkipped = skippedBibs.Count;
                response.SkippedBibNumbers = skippedBibs;
                response.Status = skippedBibs.Count > 0 ? "CompletedWithSkips" : "Completed";

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error adding participant range: {ex.Message}";
                _logger.LogError(ex, "Error adding participant range for event {EventId} and race {RaceId}",
                    eventId, raceId);
                response.Status = "Failed";
                return response;
            }
        }

        public async Task<UpdateParticipantsByBibResponse> UpdateParticipantsByBibAsync(
            string eventId,
            string raceId,
            UpdateParticipantsByBibRequest request)
        {
            var response = new UpdateParticipantsByBibResponse
            {
                Status = "Processing",
                FileName = request.File?.FileName ?? string.Empty,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                _logger.LogInformation(
                    "Starting UpdateParticipantsByBib for EventId: {EventId}, RaceId: {RaceId}, File: {FileName}",
                    decryptedEventId, decryptedRaceId, request.File?.FileName);

                // Validate file
                if (request.File == null || request.File.Length == 0)
                {
                    ErrorMessage = "File is empty or not provided";
                    _logger.LogWarning("Update failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                if (!request.File.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = "Only CSV files are supported";
                    _logger.LogWarning("Update failed: {Error}", ErrorMessage);
                    response.Status = "Failed";
                    return response;
                }

                // Validate event exists and belongs to tenant
                var eventRepo = _repository.GetRepository<Event>();
                var eventExists = await eventRepo.GetQuery(e =>
                    e.Id == decryptedEventId &&
                    e.TenantId == tenantId &&
                    e.AuditProperties.IsActive &&
                    !e.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!eventExists)
                {
                    ErrorMessage = "Event not found or you don't have access";
                    _logger.LogWarning("Update failed: Event {EventId} not found for Tenant {TenantId}",
                        decryptedEventId, tenantId);
                    response.Status = "Failed";
                    return response;
                }

                // Validate race exists
                var raceRepo = _repository.GetRepository<Race>();
                var raceExists = await raceRepo.GetQuery(r =>
                    r.Id == decryptedRaceId &&
                    r.EventId == decryptedEventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .AnyAsync();

                if (!raceExists)
                {
                    ErrorMessage = "Race not found";
                    _logger.LogWarning("Update failed: Race {RaceId} not found for Event {EventId}",
                        decryptedRaceId, decryptedEventId);
                    response.Status = "Failed";
                    return response;
                }

                // Parse CSV file
                var csvRecords = await ParseCsvForUpdateAsync(request.File);

                if (csvRecords.Count == 0)
                {
                    ErrorMessage = "No valid records found in CSV file";
                    _logger.LogWarning("Update failed: No valid records in CSV");
                    response.Status = "Failed";
                    return response;
                }

                // Get existing participants for this event/race
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var existingParticipants = await participantRepo.GetQuery(p =>
                    p.EventId == decryptedEventId &&
                    p.RaceId == decryptedRaceId &&
                    p.TenantId == tenantId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .ToListAsync();

                // Create a dictionary for fast lookup by bib number
                var participantsByBib = existingParticipants
                    .Where(p => !string.IsNullOrWhiteSpace(p.BibNumber))
                    .ToDictionary(p => p.BibNumber!, p => p, StringComparer.OrdinalIgnoreCase);

                var updatedCount = 0;
                var notFoundBibs = new List<string>();
                var skippedCount = 0;

                await _repository.BeginTransactionAsync();

                try
                {
                    foreach (var record in csvRecords)
                    {
                        // Skip records without bib number
                        if (string.IsNullOrWhiteSpace(record.BibNumber))
                        {
                            skippedCount++;
                            response.Errors.Add(new ValidationError
                            {
                                RowNumber = record.RowNumber,
                                Field = "BibNumber",
                                Message = "BIB number is missing",
                                Value = string.Empty
                            });
                            continue;
                        }

                        // Find existing participant by bib number
                        if (!participantsByBib.TryGetValue(record.BibNumber, out var participant))
                        {
                            notFoundBibs.Add(record.BibNumber);
                            continue;
                        }

                        // Apply updates using helper method
                        if (ApplyUpdateToParticipant(participant, record))
                        {
                            participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            participant.AuditProperties.UpdatedBy = userId;

                            await participantRepo.UpdateAsync(participant);
                            updatedCount++;
                        }
                    }

                    await _repository.SaveChangesAsync();
                    await _repository.CommitTransactionAsync();

                    _logger.LogInformation(
                        "UpdateParticipantsByBib completed. Updated: {Updated}, NotFound: {NotFound}, Skipped: {Skipped}",
                        updatedCount, notFoundBibs.Count, skippedCount);
                }
                catch (Exception ex)
                {
                    await _repository.RollbackTransactionAsync();
                    throw;
                }

                response.TotalUpdated = updatedCount;
                response.TotalNotFound = notFoundBibs.Count;
                response.TotalSkipped = skippedCount;
                response.NotFoundBibNumbers = notFoundBibs;
                response.Status = (notFoundBibs.Count > 0 || skippedCount > 0) ? "CompletedWithWarnings" : "Completed";

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating participants by bib: {ex.Message}";
                _logger.LogError(ex, "Error updating participants by bib for event {EventId} and race {RaceId}",
                    eventId, raceId);
                response.Status = "Failed";
                return response;
            }
        }

        /// <summary>
        /// Parses CSV file for update operation, extracting participant details
        /// </summary>
        private async Task<List<ParticipantUpdateRecord>> ParseCsvForUpdateAsync(Microsoft.AspNetCore.Http.IFormFile file)
        {
            var records = new List<ParticipantUpdateRecord>();
            var rowNumber = 0;

            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);

            // Read header
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return records;
            }

            var headers = headerLine.Split(',').Select(h => h.Trim().ToLower()).ToArray();

            // Find column indices (flexible mapping)
            var bibIndex = FindColumnIndex(headers, "bib", "bib number", "bibnumber", "number");
            var firstNameIndex = FindColumnIndex(headers, "first name", "firstname", "first", "name");
            var lastNameIndex = FindColumnIndex(headers, "last name", "lastname", "last", "surname");
            var emailIndex = FindColumnIndex(headers, "email", "e-mail", "mail");
            var phoneIndex = FindColumnIndex(headers, "phone", "mobile", "contact", "mobile number", "phone number");
            var genderIndex = FindColumnIndex(headers, "gender", "sex");
            var ageCategoryIndex = FindColumnIndex(headers, "age category", "category", "age group", "agecategory");
            var countryIndex = FindColumnIndex(headers, "country", "nation", "nationality");
            var cityIndex = FindColumnIndex(headers, "city", "town");
            var tshirtIndex = FindColumnIndex(headers, "tshirt", "t-shirt", "shirt size", "tshirt size", "t-shirt size");
            var dobIndex = FindColumnIndex(headers, "dob", "date of birth", "dateofbirth", "birthdate", "birth date");

            // Read data rows
            while (!reader.EndOfStream)
            {
                rowNumber++;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = ParseCsvLine(line);

                var record = new ParticipantUpdateRecord
                {
                    RowNumber = rowNumber,
                    BibNumber = GetValueAtIndex(values, bibIndex),
                    FirstName = GetValueAtIndex(values, firstNameIndex),
                    LastName = GetValueAtIndex(values, lastNameIndex),
                    Email = GetValueAtIndex(values, emailIndex),
                    Phone = GetValueAtIndex(values, phoneIndex),
                    Gender = GetValueAtIndex(values, genderIndex),
                    AgeCategory = GetValueAtIndex(values, ageCategoryIndex),
                    Country = GetValueAtIndex(values, countryIndex),
                    City = GetValueAtIndex(values, cityIndex),
                    TShirtSize = GetValueAtIndex(values, tshirtIndex)
                };

                // Parse date of birth if present
                var dobValue = GetValueAtIndex(values, dobIndex);
                if (!string.IsNullOrWhiteSpace(dobValue) && DateTime.TryParse(dobValue, out var dob))
                {
                    record.DateOfBirth = dob;
                }

                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Applies update record data to participant entity
        /// </summary>
        private static bool ApplyUpdateToParticipant(Models.Data.Entities.Participant participant, ParticipantUpdateRecord record)
        {
            var hasChanges = false;

            if (!string.IsNullOrWhiteSpace(record.FirstName))
            {
                participant.FirstName = record.FirstName;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.LastName))
            {
                participant.LastName = record.LastName;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.Email))
            {
                participant.Email = record.Email;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.Phone))
            {
                participant.Phone = record.Phone;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.Gender))
            {
                participant.Gender = record.Gender;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.AgeCategory))
            {
                participant.AgeCategory = record.AgeCategory;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.Country))
            {
                participant.Country = record.Country;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.City))
            {
                participant.City = record.City;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.TShirtSize))
            {
                participant.TShirtSize = record.TShirtSize;
                hasChanges = true;
            }

            if (record.DateOfBirth.HasValue)
            {
                participant.DateOfBirth = record.DateOfBirth;
                hasChanges = true;
            }

            return hasChanges;
        }

        /// <summary>
        /// Builds a bib number with optional prefix and suffix
        /// </summary>
        private static string BuildBibNumber(string? prefix, int number, string? suffix)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                sb.Append(prefix.Trim());
            }

            sb.Append(number);

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                sb.Append(suffix.Trim());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the filter expression for event search
        /// </summary>
        private static Expression<Func<Models.Data.Entities.Participant, bool>> BuildSearchExpression(ParticipantSearchRequest request, int eventId, int raceId)
        {
            return e =>
                e.EventId == eventId &&
                e.RaceId == raceId &&
                (!request.Status.HasValue || e.Status == request.Status.Value.ToString()) &&
                (!request.Gender.HasValue || e.Gender == request.Gender.Value.ToString()) &&
                (string.IsNullOrEmpty(request.Category) || e.AgeCategory == request.Category) &&
                e.AuditProperties.IsActive &&
                !e.AuditProperties.IsDeleted;
        }

        /// <summary>
        /// Combines the base expression with a free-text search across multiple fields (BibNumber, FirstName, LastName, Email, Mobile).
        /// The combined expression is translated to SQL by EF Core.
        /// </summary>
        private static Expression<Func<Models.Data.Entities.Participant, bool>> CombineExpressionWithSearch(Expression<Func<Models.Data.Entities.Participant, bool>> baseExpression, string search)
        {
            // Build per-field contains expressions: protect against nulls
            var param = Expression.Parameter(typeof(Models.Data.Entities.Participant), "e");

            // Replace parameter in base expression
            var leftVisitor = new ReplaceParameterVisitor(baseExpression.Parameters[0], param);
            var leftBody = leftVisitor.Visit(baseExpression.Body)!;

            // Prepare method info for string.Contains
            var containsMethod = typeof(string).GetMethod("Contains", [typeof(string)])!;

            Expression? orExpression = null;

            // Helper to build (property != null && property.Contains(search))
            Expression BuildContains(string propertyName)
            {
                var prop = Expression.PropertyOrField(param, propertyName);
                var notNull = Expression.NotEqual(prop, Expression.Constant(null, typeof(string)));
                var call = Expression.Call(prop, containsMethod, Expression.Constant(search));
                return Expression.AndAlso(notNull, call);
            }

            // Fields to search - adjust names if your Participant entity uses different property names
            var fields = new[] { "BibNumber", "FirstName", "LastName", "Email", "Phone", "AgeCategory" };

            foreach (var field in fields)
            {
                try
                {
                    var containsExpr = BuildContains(field);
                    orExpression = orExpression == null ? containsExpr : Expression.OrElse(orExpression, containsExpr);
                }
                catch (ArgumentException)
                {
                    // If the property doesn't exist on the Participant entity, skip it gracefully
                    continue;
                }
            }

            // If no searchable fields exist, return the base expression unchanged
            if (orExpression == null)
            {
                return baseExpression;
            }

            // Combine base (AND) with OR-of-fields
            var combinedBody = Expression.AndAlso(leftBody, orExpression);

            return Expression.Lambda<Func<Models.Data.Entities.Participant, bool>>(combinedBody, param);
        }

        private class ReplaceParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly ParameterExpression _newParam;

            public ReplaceParameterVisitor(ParameterExpression oldParam, ParameterExpression newParam)
            {
                _oldParam = oldParam;
                _newParam = newParam;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _oldParam ? _newParam : base.VisitParameter(node);
            }
        }

        private async Task<List<ParticipantStaging>> ParseCsvFileAsync(IFormFile file)
        {
            var records = new List<ParticipantStaging>();
            var rowNumber = 0;

            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);

            // Read header
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return records;
            }

            var headers = headerLine.Split(',').Select(h => h.Trim().ToLower()).ToArray();

            // Find column indices (flexible mapping)
            var bibIndex = FindColumnIndex(headers, "bib", "bib number", "number");
            var nameIndex = FindColumnIndex(headers, "name", "participant name", "full name", "firstname");
            var genderIndex = FindColumnIndex(headers, "gender", "sex");
            var ageCategoryIndex = FindColumnIndex(headers, "age category", "category", "age group");
            var emailIndex = FindColumnIndex(headers, "email", "e-mail");
            var mobileIndex = FindColumnIndex(headers, "mobile", "phone", "contact", "mobile number");

            // Read data rows
            while (!reader.EndOfStream)
            {
                rowNumber++;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = ParseCsvLine(line);

                var record = new ParticipantStaging
                {
                    RowNumber = rowNumber,
                    Bib = GetValueAtIndex(values, bibIndex),
                    FirstName = GetValueAtIndex(values, nameIndex),
                    Gender = GetValueAtIndex(values, genderIndex),
                    AgeCategory = GetValueAtIndex(values, ageCategoryIndex),
                    Email = GetValueAtIndex(values, emailIndex),
                    Mobile = GetValueAtIndex(values, mobileIndex),
                    ProcessingStatus = "Pending"
                };

                records.Add(record);
            }

            return records;
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString().Trim());
            return result.ToArray();
        }

        private int FindColumnIndex(string[] headers, params string[] possibleNames)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                foreach (var name in possibleNames)
                {
                    if (headers[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private string? GetValueAtIndex(string[] values, int index)
        {
            if (index < 0 || index >= values.Length)
                return null;

            var value = values[index]?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private List<ValidationError> ValidateRecord(ParticipantStaging record)
        {
            var errors = new List<ValidationError>();

            if (string.IsNullOrWhiteSpace(record.Bib))
            {
                errors.Add(new ValidationError
                {
                    RowNumber = record.RowNumber,
                    Field = "Bib",
                    Message = "BIB number is required",
                    Value = record.Bib ?? ""
                });
            }

            return errors;
        }

        /// <summary>
        /// Maps sort field name from client format to database format
        /// </summary>
        private string? GetMappedSortField(string? sortFieldName)
        {
            if (string.IsNullOrEmpty(sortFieldName))
            {
                return null;
            }

            if (SortFieldMapping.TryGetValue(sortFieldName, out var dbFieldName))
            {
                return dbFieldName;
            }

            _logger.LogWarning("Unknown sort field '{SortField}' requested, using default sorting", sortFieldName);
            return "Id";
        }

        // Map client-facing property names to database property names
        private static readonly Dictionary<string, string> SortFieldMapping = new(StringComparer.OrdinalIgnoreCase)
        {
             { "CreatedAt", "AuditProperties.CreatedDate" },
             { "UpdatedAt", "AuditProperties.UpdatedDate" }
        };
    }
}