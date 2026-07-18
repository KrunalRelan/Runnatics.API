using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Client.Responses.Export;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Client.Responses.Races;
using Runnatics.Models.Data.Constants;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using Runnatics.Services.Helpers;
using System.Linq.Expressions;
using System.Text;

namespace Runnatics.Services
{
    public class ParticipantImportService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<ParticipantImportService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService,
        ICategoryNormalizer categoryNormalizer) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IParticipantImportService
    {
        protected readonly IMapper _mapper = mapper;
        private readonly ILogger<ParticipantImportService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;
        private readonly ICategoryNormalizer _categoryNormalizer = categoryNormalizer;

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
                        record.ProcessingStatus = "Invalid";
                        record.ErrorMessage = string.Join("; ", validationErrors.Select(e => e.Message));
                        foreach (var error in validationErrors)
                        {
                            response.Errors.Add(error);
                        }
                        _logger.LogWarning("Row {Row}: validation failed — {Errors}",
                            record.RowNumber, record.ErrorMessage);
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

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                // Pre-load existing bibs for duplicate detection
                // Parentheses are critical — without them, IsActive/IsDeleted
                // filters were skipped when raceId was provided (operator precedence bug).
                var existingBibs = await participantRepo
                    .GetQuery(p =>
                        p.EventId == decryptedEventId &&
                        (!raceId.HasValue || p.RaceId == raceId.Value) &&
                        p.AuditProperties.IsActive &&
                        !p.AuditProperties.IsDeleted)
                    .Select(p => p.BibNumber)
                    .AsNoTracking()
                    .ToListAsync();

                var existingBibSet = new HashSet<string>(
                    existingBibs.Where(b => b != null)!,
                    StringComparer.OrdinalIgnoreCase);

                var successCount = 0;
                var errorCount = 0;

                foreach (var record in stagingRecords)
                {
                    try
                    {
                        // Duplicate bib check
                        if (!string.IsNullOrWhiteSpace(record.Bib) && existingBibSet.Contains(record.Bib))
                        {
                            record.ProcessingStatus = "Error";
                            record.ErrorMessage = $"Bib '{record.Bib}' already exists in this race.";
                            errorCount++;
                            response.Errors.Add(new ProcessingError
                            {
                                StagingId = record.Id,
                                RowNumber = record.RowNumber,
                                Bib = record.Bib,
                                Name = record.FirstName,
                                ErrorMessage = record.ErrorMessage
                            });
                            _logger.LogWarning("Row {Row}: duplicate bib '{Bib}'", record.RowNumber, record.Bib);
                            continue;
                        }

                        var ageCategory = await _categoryNormalizer.ResolveAgeCategoryAsync(decryptedEventId, record.AgeCategory);

                        var participant = new Models.Data.Entities.Participant
                        {
                            TenantId = tenantId,
                            EventId = decryptedEventId,
                            RaceId = raceId ?? 0,
                            BibNumber = record.Bib,
                            FirstName = record.FirstName,
                            Email = record.Email,
                            Phone = record.Mobile,
                            Gender = string.IsNullOrWhiteSpace(record.Gender) ? "Unknown" : NormalizeGenderForWrite(record.Gender),
                            AgeCategory = ageCategory,
                            ImportBatchId = decryptedImportBatchId,
                            Status = "Registered",
                            AuditProperties = new Models.Data.Common.AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        await participantRepo.AddAsync(participant);

                        record.ProcessingStatus = "Success";
                        successCount++;

                        if (!string.IsNullOrWhiteSpace(record.Bib))
                            existingBibSet.Add(record.Bib);
                    }
                    catch (Exception ex)
                    {
                        record.ProcessingStatus = "Error";
                        record.ErrorMessage = ex.Message;
                        errorCount++;
                        response.Errors.Add(new ProcessingError
                        {
                            StagingId = record.Id,
                            RowNumber = record.RowNumber,
                            Bib = record.Bib,
                            Name = record.FirstName,
                            ErrorMessage = ex.Message
                        });
                        _logger.LogWarning(ex, "Row {Row}: processing failed for record {RecordId}", record.RowNumber, record.Id);
                    }
                }

                // Persist participants, staging status updates, and batch status in one transaction
                foreach (var record in stagingRecords)
                    await stagingRepo.UpdateAsync(record);

                importBatch.Status = errorCount == 0 ? "Completed" : "PartiallyCompleted";
                importBatch.ProcessedAt = DateTime.UtcNow;
                await batchRepo.UpdateAsync(importBatch);

                await _repository.SaveChangesAsync();

                response.SuccessCount = successCount;
                response.ErrorCount = errorCount;
                response.Status = importBatch.Status;

                _logger.LogInformation(
                    "Processing completed for ImportBatch {BatchId}. Total staged: {Total}, Success: {Success}, Errors: {Errors}",
                    decryptedImportBatchId, stagingRecords.Count, successCount, errorCount);

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
                var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();

                // Build base participant query with filters
                var participantQuery = participantRepo.GetQuery(p =>
                    p.EventId == decryptedEventId &&
                    p.RaceId == decryptedRaceId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted);

                // STATUS filter (contract 2026-07-07): matches the COMPUTED Results.Status —
                // the value the grid's Status column displays — via the join below, NEVER the
                // stale raw Participant.Status (the field behind the "Registered (computed)"
                // label bug; the old numeric contract filtered it and returned wrong rows).
                // ResultStatus.FilterToStored maps "OK"→"Finished", "DSQ"→"DQ" etc.; a filtered
                // status can only match runners WITH a Results row (unprocessed runners appear
                // under "All Status" only).
                var storedStatusFilter = Models.Data.Constants.ResultStatus.FilterToStored(request.Status);

                // Apply gender filter if provided
                if (request.Gender.HasValue)
                {
                    var genderString = MapGenderToDbString(request.Gender.Value);
                    if (genderString != null)
                        participantQuery = participantQuery.Where(p => p.Gender == genderString);
                }

                // Apply category filter if provided
                if (!string.IsNullOrEmpty(request.Category))
                {
                    participantQuery = participantQuery.Where(p => p.AgeCategory == request.Category);
                }

                // Apply free-text search if provided
                if (!string.IsNullOrWhiteSpace(request.SearchString))
                {
                    var search = request.SearchString.Trim();
                    participantQuery = participantQuery.Where(p =>
                        (p.BibNumber != null && p.BibNumber.Contains(search)) ||
                        (p.FirstName != null && p.FirstName.Contains(search)) ||
                        (p.LastName != null && p.LastName.Contains(search)) ||
                        (p.Email != null && p.Email.Contains(search)) ||
                        (p.Phone != null && p.Phone.Contains(search)) ||
                        (p.AgeCategory != null && p.AgeCategory.Contains(search))
                    );
                }

                // Left join with Results to get rank-based sorting
                var resultsQuery = resultsRepo.GetQuery(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId);

                var joinedQuery = participantQuery
                    .GroupJoin(
                        resultsQuery,
                        p => p.Id,
                        r => r.ParticipantId,
                        (p, results) => new { Participant = p, Results = results })
                    .SelectMany(
                        x => x.Results.DefaultIfEmpty(),
                        (x, r) => new
                        {
                            x.Participant,
                            Status = r != null ? r.Status : null,
                            OverallRank = r != null ? r.OverallRank : (int?)null,
                            GunTime = r != null ? r.GunTime : (long?)null,
                            // #7/#5 sort priority: OK(Finished)=0, DNF=1, DNS=2, DSQ=3 (LAST of the
                            // statuses), no result=4
                            StatusOrder = r == null ? 4 :
                                r.Status == "Finished" ? 0 :
                                r.Status == "DNF" ? 1 :
                                r.Status == "DNS" ? 2 :
                                r.Status == "DQ" ? 3 : 4
                        });

                // The status filter lives on the JOINED shape — it matches the computed
                // Results.Status the grid displays.
                if (storedStatusFilter != null)
                    joinedQuery = joinedQuery.Where(x => x.Status == storedStatusFilter);

                // Apply sorting: Status priority first, then by GunTime asc within each group, then by Bib
                var orderedQuery = joinedQuery
                    .OrderBy(x => x.StatusOrder)
                    .ThenBy(x => x.GunTime ?? long.MaxValue)
                    .ThenBy(x => x.Participant.BibNumber == null ? 0 : x.Participant.BibNumber.Length)
                    .ThenBy(x => x.Participant.BibNumber);

                // Total count for pagination — counted on the FILTERED joined query so
                // "Showing X of Y" always matches the rendered set (a status filter would
                // otherwise report the unfiltered participant count).
                var totalCount = await joinedQuery.CountAsync();

                // Apply pagination
                var pagedResults = await orderedQuery
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => x.Participant)
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogInformation(
                    "Found {TotalCount} participants, returning page {PageNumber} with {PageItems} items",
                    totalCount,
                    pageNumber,
                    pagedResults.Count);

                // Map to response
                var toReturn = new PagingList<ParticipantSearchReponse>();
                var mappedItems = _mapper.Map<List<ParticipantSearchReponse>>(pagedResults);
                toReturn.AddRange(mappedItems);
                toReturn.TotalCount = totalCount;

                // Fetch checkpoint times for the participants in this page
                if (toReturn.Count > 0)
                {
                    await PopulateCheckpointTimesAsync(toReturn, decryptedEventId, decryptedRaceId);
                }

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

        /// <summary>
        /// Populates checkpoint times, chip IDs, and results data for each participant in the list.
        /// Merges child checkpoint times by parent checkpoint, returning the best (earliest) time.
        /// Returns null values gracefully when no checkpoint data exists.
        /// </summary>
        private async Task PopulateCheckpointTimesAsync(
            IEnumerable<ParticipantSearchReponse> participants,
            int eventId,
            int raceId)
        {
            try
            {
                // Get participant IDs (decrypt them)
                var participantIds = participants
                    .Where(p => !string.IsNullOrEmpty(p.Id))
                    .Select(p => Convert.ToInt32(_encryptionService.Decrypt(p.Id!)))
                    .ToList();

                if (participantIds.Count == 0)
                    return;

                // Fetch chip assignments for these participants
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var chipAssignments = await chipAssignmentRepo.GetQuery(ca =>
                    participantIds.Contains(ca.ParticipantId) &&
                    ca.UnassignedAt == null &&
                    ca.AuditProperties.IsActive &&
                    !ca.AuditProperties.IsDeleted)
                    .Include(ca => ca.Chip)
                    .AsNoTracking()
                    .ToListAsync();

                // Create lookup: participantId -> chipEPC
                var chipLookup = chipAssignments
                    .GroupBy(ca => ca.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.First().Chip?.EPC);

                // ========== CHECKPOINT LOGIC ==========

                // Get ALL checkpoints for this race
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var allCheckpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == raceId &&
                    c.EventId == eventId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // Order checkpoints by distance
                var checkpoints = allCheckpoints
                    .OrderBy(c => c.DistanceFromStart)
                    .ToList();

                _logger.LogDebug(
                    "Found {TotalCheckpoints} checkpoints for race {RaceId}",
                    checkpoints.Count, raceId);

                // Get all normalized readings for these participants
                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var readings = await normalizedRepo.GetQuery(r =>
                    r.EventId == eventId &&
                    participantIds.Contains(r.ParticipantId) &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                // Group readings by participant
                var readingsByParticipant = readings
                    .GroupBy(r => r.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // ========== RESULTS DATA ==========
                var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                var results = await resultsRepo.GetQuery(r =>
                    r.EventId == eventId &&
                    r.RaceId == raceId &&
                    participantIds.Contains(r.ParticipantId))
                    .AsNoTracking()
                    .ToDictionaryAsync(r => r.ParticipantId, r => r);

                _logger.LogDebug(
                    "Found {TotalResults} results for {TotalParticipants} participants",
                    results.Count, participantIds.Count);

                // Resolve the event's display timezone once before the participant loop
                var eventRepo = _repository.GetRepository<Event>();
                var eventTimeZoneId = await eventRepo.GetQuery(e => e.Id == eventId)
                    .AsNoTracking()
                    .Select(e => e.TimeZone)
                    .FirstOrDefaultAsync() ?? "UTC";

                TimeZoneInfo displayTimeZone;
                try
                {
                    displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogWarning("Event {EventId} has unknown timezone '{TZ}', falling back to UTC", eventId, eventTimeZoneId);
                    displayTimeZone = TimeZoneInfo.Utc;
                }

                // Populate checkpoint times, chip IDs, and results for each participant
                foreach (var participant in participants)
                {
                    participant.CheckpointTimes = new Dictionary<string, string?>();

                    if (string.IsNullOrEmpty(participant.Id))
                        continue;

                    var participantId = Convert.ToInt32(_encryptionService.Decrypt(participant.Id));

                    // Set chip ID and EPC mapped status
                    participant.ChipId = chipLookup.TryGetValue(participantId, out var chipEpc) ? chipEpc : null;
                    participant.IsEpcMapped = !string.IsNullOrEmpty(participant.ChipId);

                    // ========== POPULATE RESULTS DATA ==========
                    if (results.TryGetValue(participantId, out var result))
                    {
                        // #4: the grid / Run-Status DDL shows the COMPUTED result status in its
                        // DISPLAY form (OK/DNF/DNS/DSQ) — the raw stored "Finished" matched no DDL
                        // option and the control fell back to the stale Participant.Status.
                        participant.Status = MapResultStatus(result.Status);

                        // Gun Time (total time from race start)
                        if (result.GunTime.HasValue)
                        {
                            participant.GunTime = FormatDuration(result.GunTime.Value);
                        }

                        // Net/Chip Time (time minus start delay)
                        if (result.NetTime.HasValue)
                        {
                            participant.NetTime = FormatDuration(result.NetTime.Value);
                        }
                        else if (result.GunTime.HasValue)
                        {
                            // Fallback: if no net time, use gun time
                            participant.NetTime = FormatDuration(result.GunTime.Value);
                        }

                        // Rankings
                        participant.OverallRank = result.OverallRank;
                        participant.GenderRank = result.GenderRank;
                        participant.CategoryRank = result.CategoryRank;
                    }
                    else
                    {
                        // No results yet - participant hasn't finished or data not processed
                        // Keep existing status if set, otherwise mark as Registered
                        if (string.IsNullOrEmpty(participant.Status))
                        {
                            participant.Status = "Registered";
                        }
                        participant.GunTime = null;
                        participant.NetTime = null;
                        participant.OverallRank = null;
                        participant.GenderRank = null;
                        participant.CategoryRank = null;
                    }

                    // If no checkpoints exist, skip checkpoint times
                    if (checkpoints.Count == 0)
                        continue;

                    // Get readings for this participant (or empty list if none)
                    var participantReadings = readingsByParticipant.TryGetValue(participantId, out var pr)
                        ? pr
                        : new List<ReadNormalized>();

                    // Create lookup for this participant's readings by checkpoint
                    var readingsByCheckpoint = participantReadings
                        .GroupBy(r => r.CheckpointId)
                        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.ChipTime).First()); // Keep earliest if multiple

                    // Add checkpoint times (converted to event's timezone)
                    var checkpointList = new List<CheckpointTimeDto>();
                    int order = 1;
                    foreach (var checkpoint in checkpoints)
                    {
                        var checkpointName = checkpoint.Name ?? $"CP {checkpoint.DistanceFromStart}";
                        string? formattedTime = null;

                        if (readingsByCheckpoint.TryGetValue(checkpoint.Id, out var reading))
                        {
                            var localTime = TimeZoneInfo.ConvertTimeFromUtc(reading.ChipTime, displayTimeZone);
                            formattedTime = localTime.ToString("HH:mm:ss");
                        }

                        participant.CheckpointTimes[checkpointName] = formattedTime;
                        checkpointList.Add(new CheckpointTimeDto
                        {
                            CheckpointName = checkpointName,
                            CheckpointOrder = order++,
                            Time = formattedTime
                        });
                    }
                    participant.Checkpoints = checkpointList;
                }

                _logger.LogInformation(
                    "Populated checkpoint times and results for {Count} participants with {CheckpointCount} checkpoints",
                    participants.Count(), checkpoints.Count);
            }
            catch (Exception ex)
            {
                // Log error but don't fail the search - just leave checkpoint times empty
                _logger.LogWarning(ex, "Failed to populate checkpoint times for participants. Continuing with null values.");

                foreach (var participant in participants)
                {
                    participant.CheckpointTimes ??= new Dictionary<string, string?>();
                }
            }
        }

        /// <summary>
        /// Formats milliseconds duration to HH:mm:ss or mm:ss format
        /// </summary>
        private static string FormatDuration(long milliseconds)
        {
            var timeSpan = TimeSpan.FromMilliseconds(milliseconds);

            // If over 1 hour: HH:mm:ss
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";

            // If under 1 hour: mm:ss
            return $"{timeSpan.Minutes}:{timeSpan.Seconds:D2}";
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
                participant.Gender = string.IsNullOrWhiteSpace(participant.Gender) ? "Unknown" : NormalizeGenderForWrite(participant.Gender);
                participant.AgeCategory = await _categoryNormalizer.ResolveAgeCategoryAsync(eventIdInt, participant.AgeCategory);

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
                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    await participantRepo.AddAsync(participant);
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error inserting participants: {ex.Message}";
                _logger.LogError(ex, "Error while inserting the participant {bibnumber}", addParticipant.BibNumber);
            }
        }

        public async Task EditParticipant(String participantId, ParticipantRequest editParticipant)
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
                existingParticipant.Gender = string.IsNullOrWhiteSpace(existingParticipant.Gender) ? "Unknown" : NormalizeGenderForWrite(existingParticipant.Gender);
                existingParticipant.AgeCategory = await _categoryNormalizer.ResolveAgeCategoryAsync(existingParticipant.EventId, existingParticipant.AgeCategory);

                ///If existing race id is different than the new race id, then user is moving participant to another race.
                ///so, we need to delete that record from existing and add a new record in the new race.
                if (existingParticipant.RaceId != decryptedRaceId)
                {
                    // BUG-06: race move — migrate ALL timing data (Results/SplitTimes/ReadNormalized
                    // with CheckpointId remap + ChipAssignment) and reprocess the target race via the
                    // shared migration, instead of moving only the participant row + chips.
                    // NOTE: decryptedRaceId is the TARGET race here (from the request);
                    // existingParticipant.RaceId is the source. Scalar edits were already applied to
                    // existingParticipant above via _mapper.Map, so the new participant inherits them.
                    await MoveParticipantToRaceAsync(existingParticipant, decryptedRaceId);
                    return;
                }
                else
                {
                    existingParticipant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    existingParticipant.AuditProperties.IsActive = true;
                    existingParticipant.AuditProperties.IsDeleted = false;
                }

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    await participantRepo.UpdateAsync(existingParticipant);
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating participants: {ex.Message}";
                _logger.LogError(ex, "Error while updating the participant {participantId}", participantId);
            }
        }

        public async Task DeleteParicipant(String participantId)
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

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    await participantRepo.UpdateAsync(existingParticipant);
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error delete participants: {ex.Message}";
                _logger.LogError(ex, "Error while deleting the participant {participantId}", participantId);
            }
        }

        public async Task<List<Category>> GetCategories(String eventId, String raceId)
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

                var existingBibSet = new HashSet<string>(existingBibs.Where(b => b != null)!, StringComparer.OrdinalIgnoreCase);

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
                    await _repository.ExecuteInTransactionAsync(async () =>
                    {
                        foreach (var participant in participantsToAdd)
                        {
                            await participantRepo.AddAsync(participant);
                        }
                    });

                    _logger.LogInformation(
                        "Successfully added {Count} participants for EventId: {EventId}, RaceId: {RaceId}",
                        participantsToAdd.Count, decryptedEventId, decryptedRaceId);
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

                await _repository.ExecuteInTransactionAsync(async () =>
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

                        // Normalize AgeCategory casing to the event's canonical before applying.
                        if (!string.IsNullOrWhiteSpace(record.AgeCategory))
                            record.AgeCategory = await _categoryNormalizer.ResolveAgeCategoryAsync(decryptedEventId, record.AgeCategory);

                        // Apply updates using helper method
                        if (ApplyUpdateToParticipant(participant, record))
                        {
                            participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            participant.AuditProperties.UpdatedBy = userId;

                            await participantRepo.UpdateAsync(participant);
                            updatedCount++;
                        }
                    }
                });

                _logger.LogInformation(
                    "UpdateParticipantsByBib completed. Updated: {Updated}, NotFound: {NotFound}, Skipped: {Skipped}",
                    updatedCount, notFoundBibs.Count, skippedCount);

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
                participant.Gender = NormalizeGenderForWrite(record.Gender);
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(record.AgeCategory))
            {
                participant.AgeCategory = record.AgeCategory.Trim();
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

        // BuildSearchExpression(ParticipantSearchRequest…) and MapRaceStatusToDbString were
        // REMOVED 2026-07-07 (status-filter contract change): both were dead code wired to the
        // old numeric status enum filtering the stale raw Participant.Status. The live filter
        // is ResultStatus.FilterToStored → Results.Status in Search().

        private static string? MapGenderToDbString(Gender gender) => gender switch
        {
            Gender.Male => "M",
            Gender.Female => "F",
            _ => null
        };

        // Normalizes human-entered gender strings to canonical "M"/"F".
        // Unknown/blank/Other values are passed through unchanged (the EF converter is the safety net for reads).
        private static string NormalizeGenderForWrite(string raw) => raw.Trim().ToUpperInvariant() switch
        {
            "M" or "MALE" => "M",
            "F" or "FEMALE" => "F",
            _ => raw.Trim()
        };

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

        /// <summary>
        /// Get detailed participant information including performance, rankings, split times and pace progression
        /// </summary>
        public async Task<ParticipantDetailsResponse?> GetParticipantDetails(string eventId, string raceId, string participantId)
        {
            try
            {
                var tenantId = _userContext.TenantId;
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                _logger.LogInformation(
                    "Getting participant details for ParticipantId: {ParticipantId}, EventId: {EventId}, RaceId: {RaceId}",
                    decryptedParticipantId, decryptedEventId, decryptedRaceId);

                // Get participant with related data
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.EventId == decryptedEventId &&
                    p.RaceId == decryptedRaceId &&
                    p.TenantId == tenantId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .Include(p => p.Event)
                    .Include(p => p.Race)
                        .ThenInclude(r => r.RaceSettings) // LateStartCutOff → net split baseline
                    .Include(p => p.Result)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    _logger.LogWarning("Participant {ParticipantId} not found for Event {EventId}", 
                        decryptedParticipantId, decryptedEventId);
                    return null;
                }

                // Get split times with checkpoint info
                var splitTimeRepo = _repository.GetRepository<SplitTimes>();
                var splitTimes = await splitTimeRepo.GetQuery(st =>
                    st.ParticipantId == decryptedParticipantId &&
                    !st.AuditProperties.IsDeleted)
                    .Include(st => st.ToCheckpoint)
                    .Include(st => st.ReadNormalized)
                    .OrderBy(st => st.ToCheckpoint.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                // Get total counts for rankings
                var resultRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                var totalParticipantsInRace = await resultRepo.CountAsync(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId &&
                    r.Status == "Finished" &&
                    !r.AuditProperties.IsDeleted);

                var totalInGender = await resultRepo.CountAsync(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId &&
                    r.Status == "Finished" &&
                    r.Participant.Gender == participant.Gender &&
                    !r.AuditProperties.IsDeleted);

                var totalInCategory = await resultRepo.CountAsync(r =>
                    r.EventId == decryptedEventId &&
                    r.RaceId == decryptedRaceId &&
                    r.Status == "Finished" &&
                    r.Participant.AgeCategory == participant.AgeCategory &&
                    !r.AuditProperties.IsDeleted);

                // Build response using helper
                var responseBuilder = new ParticipantDetailsResponseBuilder(_mapper);
                var response = responseBuilder.BuildResponse(
                    participant,
                    splitTimes,
                    totalParticipantsInRace,
                    totalInGender,
                    totalInCategory);

                return response;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error getting participant details: {ex.Message}";
                _logger.LogError(ex, "Error getting participant details for {ParticipantId}", participantId);
                return null;
            }
        }

        public async Task<byte[]?> ExportParticipantsAsync(string raceId)
        {
            try
            {
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var splitTimesRepo = _repository.GetRepository<SplitTimes>();
                var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();

                var participants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .OrderBy(p => p.BibNumber == null ? 0 : p.BibNumber.Length)
                    .ThenBy(p => p.BibNumber)
                    .AsNoTracking()
                    .ToListAsync();

                var checkpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderBy(c => c.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                var participantIds = participants.Select(p => p.Id).ToList();

                var results = await resultsRepo.GetQuery(r =>
                    r.RaceId == decryptedRaceId &&
                    participantIds.Contains(r.ParticipantId))
                    .AsNoTracking()
                    .ToDictionaryAsync(r => r.ParticipantId);

                var splits = await splitTimesRepo.GetQuery(st =>
                    participantIds.Contains(st.ParticipantId) &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var splitsByParticipant = splits
                    .GroupBy(st => st.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var ws = workbook.Worksheets.Add("Participants");

                var headers = new List<string> { "BIB No", "Name", "Mobile", "Email", "Gender", "Age Category", "Status", "Gun Time", "Chip Time" };
                headers.AddRange(checkpoints.Select(c => $"{c.Name} Time"));

                for (int i = 0; i < headers.Count; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                    ws.Cell(1, i + 1).Style.Font.Bold = true;
                }

                int row = 2;
                foreach (var p in participants)
                {
                    results.TryGetValue(p.Id, out var result);
                    ws.Cell(row, 1).Value = p.BibNumber ?? string.Empty;
                    ws.Cell(row, 2).Value = $"{p.FirstName} {p.LastName}".Trim();
                    ws.Cell(row, 3).Value = p.Phone ?? string.Empty;
                    ws.Cell(row, 4).Value = p.Email ?? string.Empty;
                    ws.Cell(row, 5).Value = p.Gender ?? string.Empty;
                    ws.Cell(row, 6).Value = p.AgeCategory ?? string.Empty;
                    ws.Cell(row, 7).Value = MapResultStatus(result?.Status ?? p.Status); // #7: "Finished" → "OK"
                    ws.Cell(row, 8).Value = result?.GunTime.HasValue == true ? FormatDuration(result.GunTime!.Value) : string.Empty;
                    ws.Cell(row, 9).Value = result?.NetTime.HasValue == true ? FormatDuration(result.NetTime!.Value) : string.Empty;

                    var participantSplits = splitsByParticipant.TryGetValue(p.Id, out var ps) ? ps : new List<SplitTimes>();
                    for (int c = 0; c < checkpoints.Count; c++)
                    {
                        var cpSplit = participantSplits
                            .Where(st => st.CheckpointId == checkpoints[c].Id || st.ToCheckpointId == checkpoints[c].Id)
                            .OrderBy(st => st.SplitTimeMs)
                            .FirstOrDefault();
                        ws.Cell(row, 10 + c).Value = cpSplit?.SplitTimeMs.HasValue == true
                            ? FormatDuration(cpSplit.SplitTimeMs!.Value)
                            : string.Empty;
                    }
                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error exporting participants: {ex.Message}";
                _logger.LogError(ex, "Error exporting participants for race {RaceId}", raceId);
                return null;
            }
        }

        public async Task<ExcelExportResult?> ExportParticipantsDetailedAsync(string eventId, string raceId)
        {
            try
            {
                var decryptedEventId = Convert.ToInt32(_encryptionService.Decrypt(eventId));
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));

                var raceRepo = _repository.GetRepository<Race>();
                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
                var checkpointRepo = _repository.GetRepository<Checkpoint>();
                var splitTimesRepo = _repository.GetRepository<SplitTimes>();
                var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                var notificationRepo = _repository.GetRepository<Models.Data.Entities.Notification>();
                var eventRepo = _repository.GetRepository<Event>();

                var race = await raceRepo.GetQuery(r => r.Id == decryptedRaceId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (race == null)
                {
                    ErrorMessage = "Race not found.";
                    return null;
                }

                var eventInfo = await eventRepo.GetQuery(e => e.Id == decryptedEventId)
                    .AsNoTracking()
                    .Select(e => new { e.TimeZone, e.Name })
                    .FirstOrDefaultAsync();

                var eventTimeZoneId = eventInfo?.TimeZone ?? "UTC";
                var eventName = eventInfo?.Name;

                TimeZoneInfo displayTz;
                try
                {
                    displayTz = TimeZoneInfo.FindSystemTimeZoneById(eventTimeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogWarning("Event {EventId} has unknown timezone '{TZ}', falling back to UTC", decryptedEventId, eventTimeZoneId);
                    displayTz = TimeZoneInfo.Utc;
                }

                var participants = await participantRepo.GetQuery(p =>
                    p.RaceId == decryptedRaceId &&
                    p.EventId == decryptedEventId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var checkpoints = await checkpointRepo.GetQuery(c =>
                    c.RaceId == decryptedRaceId &&
                    c.AuditProperties.IsActive &&
                    !c.AuditProperties.IsDeleted)
                    .OrderBy(c => c.DistanceFromStart)
                    .AsNoTracking()
                    .ToListAsync();

                var participantIds = participants.Select(p => p.Id).ToList();

                var results = await resultsRepo.GetQuery(r =>
                    r.RaceId == decryptedRaceId &&
                    participantIds.Contains(r.ParticipantId))
                    .AsNoTracking()
                    .ToDictionaryAsync(r => r.ParticipantId);

                var splits = await splitTimesRepo.GetQuery(st =>
                    participantIds.Contains(st.ParticipantId) &&
                    st.AuditProperties.IsActive &&
                    !st.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var splitsByParticipant = splits
                    .GroupBy(st => st.ParticipantId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                Dictionary<int, DateTime> smsSentByParticipant;
                try
                {
                    smsSentByParticipant = (await notificationRepo.GetQuery(n =>
                        n.ParticipantId.HasValue &&
                        participantIds.Contains(n.ParticipantId!.Value) &&
                        n.Type == "SMS" &&
                        n.SentAt.HasValue)
                        .AsNoTracking()
                        .ToListAsync())
                        .GroupBy(n => n.ParticipantId!.Value)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.SentAt).First().SentAt!.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load SMS notifications for export — SMS Sent At column will be empty");
                    smsSentByParticipant = new Dictionary<int, DateTime>();
                }

                // #7/#5 sort: OK(Finished) first by NetTime, then DNF, DNS, DSQ LAST, no-result
                // trailing; bib as the stable tiebreak.
                var ordered = participants
                    .Select(p => (Participant: p, Result: results.TryGetValue(p.Id, out var r) ? r : null))
                    .OrderBy(x => x.Result == null ? 4 :
                        x.Result.Status == "Finished" ? 0 :
                        x.Result.Status == "DNF" ? 1 :
                        x.Result.Status == "DNS" ? 2 :
                        x.Result.Status == "DQ" ? 3 : 4)
                    .ThenBy(x => x.Result?.NetTime ?? long.MaxValue)
                    .ThenBy(x => x.Participant.BibNumber == null ? 0 : x.Participant.BibNumber.Length)
                    .ThenBy(x => x.Participant.BibNumber)
                    .ToList();

                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var ws = workbook.Worksheets.Add("Participants");

                var headers = new List<string> { "#", "Bib No", "Name", "Age", "Gender", "Status", "Chip Time", "Gun Time", "SMS Sent At", "Mobile", "Email" };
                headers.AddRange(checkpoints.Select(c => $"{c.Name} ({c.DistanceFromStart:G})"));

                for (int i = 0; i < headers.Count; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                    ws.Cell(1, i + 1).Style.Font.Bold = true;
                }

                int row = 2;
                foreach (var (p, result) in ordered)
                {
                    var displayStatus = MapResultStatus(result?.Status ?? p.Status);

                    ws.Cell(row, 1).Value = row - 1;
                    ws.Cell(row, 2).Value = p.BibNumber ?? string.Empty;
                    ws.Cell(row, 3).Value = $"{p.FirstName} {p.LastName}".Trim();
                    ws.Cell(row, 4).Value = p.AgeCategory ?? string.Empty;
                    ws.Cell(row, 5).Value = p.Gender ?? string.Empty;
                    ws.Cell(row, 6).Value = displayStatus;
                    ws.Cell(row, 7).Value = result?.NetTime.HasValue == true ? FormatDuration(result.NetTime!.Value) : string.Empty;
                    ws.Cell(row, 8).Value = result?.GunTime.HasValue == true ? FormatDuration(result.GunTime!.Value) : string.Empty;

                    if (smsSentByParticipant.TryGetValue(p.Id, out var smsSentAt))
                        ws.Cell(row, 9).Value = TimeZoneInfo.ConvertTimeFromUtc(smsSentAt, displayTz).ToString("HH:mm:ss");
                    else
                        ws.Cell(row, 9).Value = string.Empty;

                    ws.Cell(row, 10).Value = p.Phone ?? string.Empty;
                    ws.Cell(row, 11).Value = p.Email ?? string.Empty;

                    var participantSplits = splitsByParticipant.TryGetValue(p.Id, out var ps) ? ps : new List<SplitTimes>();
                    for (int c = 0; c < checkpoints.Count; c++)
                    {
                        var cpSplit = participantSplits
                            .Where(st => st.CheckpointId == checkpoints[c].Id || st.ToCheckpointId == checkpoints[c].Id)
                            .OrderBy(st => st.SplitTimeMs)
                            .FirstOrDefault();

                        if (cpSplit?.SplitTimeMs.HasValue == true && race.StartTime.HasValue)
                        {
                            var absUtc = race.StartTime.Value.AddMilliseconds(cpSplit.SplitTimeMs.Value);
                            ws.Cell(row, 12 + c).Value = TimeZoneInfo.ConvertTimeFromUtc(absUtc, displayTz).ToString("HH:mm:ss");
                        }
                        else
                        {
                            ws.Cell(row, 12 + c).Value = string.Empty;
                        }
                    }
                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);

                var fileName = BuildParticipantExportFileName(eventName, race.Title);

                return new ExcelExportResult
                {
                    Content = stream.ToArray(),
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    FileName = fileName,
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error exporting participants: {ex.Message}";
                _logger.LogError(ex, "Error exporting participants for event {EventId} race {RaceId}", eventId, raceId);
                return null;
            }
        }

        private static string BuildParticipantExportFileName(string? eventName, string? raceName)
        {
            static string Sanitize(string? value) =>
                string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : string.Concat(value.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');

            var parts = new[] { Sanitize(eventName), Sanitize(raceName), "participantsData", DateTime.UtcNow.ToString("yyyyMMdd") }
                .Where(p => !string.IsNullOrEmpty(p));

            return $"{string.Join("_", parts)}.xlsx";
        }

        // #7/#5 display mapping: "Finished" → "OK", "DQ" → "DSQ" (stored values unchanged).
        private static string MapResultStatus(string? status) => status switch
        {
            "Finished" => "OK",
            "DNF" => "DNF",
            "DQ" => "DSQ",
            "DNS" => "DNS",
            _ => string.Empty
        };

        public async Task<ParticipantSearchReponse?> UpdateParticipantExtendedAsync(string raceId, string participantId, UpdateParticipantRequest request)
        {
            try
            {
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));
                var tenantId = _userContext.TenantId;
                var userId = _userContext.UserId;

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.TenantId == tenantId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    _logger.LogWarning("UpdateParticipantExtended failed: Participant {ParticipantId} not found in Race {RaceId}", participantId, raceId);
                    return null;
                }

                // ============================================================
                // UN-DSQ (flagged follow-up to #4/#5): RunStatus="Recompute" clears an existing
                // disqualification. Allowed ONLY when the current stored status is DQ — every
                // other manual status write stays 400 (request validation). The status is then
                // RECLASSIFIED from gate coverage (#7, ParticipantStatusCalculator — never
                // operator choice), the reason is nulled, and the race re-ranks IN MEMORY
                // (everyone below the restored finisher steps back down).
                // ============================================================
                var clearingDsq = ResultStatus.IsClearDsq(request.RunStatus);
                string? recomputedStatus = null;
                if (clearingDsq)
                {
                    if (!string.IsNullOrEmpty(request.RaceId) &&
                        Convert.ToInt32(_encryptionService.Decrypt(request.RaceId)) != decryptedRaceId)
                    {
                        ErrorMessage = "Clearing a disqualification cannot be combined with a race move.";
                        return null;
                    }

                    var currentStatus = await _repository.GetRepository<Models.Data.Entities.Results>()
                        .GetQuery(r =>
                            r.ParticipantId == decryptedParticipantId &&
                            r.EventId == participant.EventId &&
                            r.RaceId == decryptedRaceId &&
                            !r.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .Select(r => r.Status)
                        .FirstOrDefaultAsync();

                    if (currentStatus != ResultStatus.DQ)
                    {
                        ErrorMessage = "Participant is not disqualified — there is no DSQ to clear.";
                        return null;
                    }

                    recomputedStatus = await ParticipantStatusCalculator.ComputeAsync(
                        _repository, participant.EventId, decryptedRaceId, decryptedParticipantId);
                }

                // Apply scalar updates
                if (request.FirstName != null) participant.FirstName = request.FirstName;
                if (request.LastName != null) participant.LastName = request.LastName;
                if (request.Mobile != null) participant.Phone = request.Mobile;
                if (request.Email != null) participant.Email = request.Email;
                if (request.AgeCategory != null) participant.AgeCategory = await _categoryNormalizer.ResolveAgeCategoryAsync(participant.EventId, request.AgeCategory);
                if (request.DateOfBirth.HasValue) participant.DateOfBirth = request.DateOfBirth;
                if (request.ManualDistance.HasValue) participant.ManualDistance = request.ManualDistance;
                if (request.LoopCount.HasValue) participant.LoopCount = request.LoopCount;

                // #4/#5 (2026-07-03): only DSQ is manually settable (request validation enforces
                // it); normalize every spelling to the ONE canonical stored value "DQ".
                if (request.RunStatus != null && ResultStatus.IsDsq(request.RunStatus))
                {
                    participant.Status = ResultStatus.DQ;
                }
                else if (clearingDsq)
                {
                    // UN-DSQ: the computed status (#7) replaces the manual DQ on the
                    // participant row too — status comes from gate coverage, never the operator.
                    participant.Status = recomputedStatus!;
                }

                participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                participant.AuditProperties.UpdatedBy = userId;

                // Handle race reassignment
                int targetRaceId = decryptedRaceId;
                if (!string.IsNullOrEmpty(request.RaceId))
                {
                    targetRaceId = Convert.ToInt32(_encryptionService.Decrypt(request.RaceId));
                    if (targetRaceId != decryptedRaceId)
                    {
                        // BUG-06: full timing-data migration + target-race reprocess (shared logic).
                        // Scalar edits above were applied to `participant`; the new participant copies
                        // its fields from that entity inside the shared method.
                        var movedParticipant = await MoveParticipantToRaceAsync(participant, targetRaceId);
                        if (movedParticipant == null)
                            return null; // target race invalid — ErrorMessage already set
                        return MapToSearchResponse(movedParticipant);
                    }
                }

                var dsqApplied = false;
                var dsqCleared = false;

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    await participantRepo.UpdateAsync(participant);

                    // Update Results for RunStatus/DisqualificationReason/ManualTime
                    if (request.RunStatus != null || request.DisqualificationReason != null || request.ManualTime != null)
                    {
                        var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                        var result = await resultsRepo.GetQuery(r =>
                            r.ParticipantId == decryptedParticipantId &&
                            r.RaceId == decryptedRaceId)
                            .FirstOrDefaultAsync();

                        // #5: DSQ save — normalize to the stored "DQ", null the ranks (a DSQ'd
                        // runner is never ranked), keep times for the record, and re-rank the race
                        // after the transaction. A missing Results row is created so the DSQ is
                        // visible on the grid/public site.
                        var settingDsq = request.RunStatus != null && ResultStatus.IsDsq(request.RunStatus);

                        if (result == null && settingDsq)
                        {
                            result = new Models.Data.Entities.Results
                            {
                                EventId = participant.EventId,
                                RaceId = decryptedRaceId,
                                ParticipantId = decryptedParticipantId,
                                IsOfficial = false,
                                CertificateGenerated = false,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            };
                            await resultsRepo.AddAsync(result);
                        }

                        if (result != null)
                        {
                            if (settingDsq)
                            {
                                result.Status = ResultStatus.DQ;
                                result.OverallRank = null;
                                result.GenderRank = null;
                                result.CategoryRank = null;
                                dsqApplied = true;
                            }
                            else if (clearingDsq && result.Status == ResultStatus.DQ)
                            {
                                // UN-DSQ: reclassified status (#7), reason nulled; ranks stay
                                // null here — the in-memory race-wide re-rank after the
                                // transaction assigns them when the runner is Finished.
                                result.Status = recomputedStatus!;
                                result.DisqualificationReason = null;
                                dsqCleared = true;
                            }
                            if (request.DisqualificationReason != null && !clearingDsq)
                                result.DisqualificationReason = request.DisqualificationReason;

                            if (request.ManualTime != null &&
                                TimeSpan.TryParseExact(request.ManualTime, @"hh\:mm\:ss", null, out var manualSpan))
                            {
                                result.ManualFinishTimeMs = (long)manualSpan.TotalMilliseconds;
                            }

                            result.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            result.AuditProperties.UpdatedBy = userId;
                            if (result.Id != 0)
                                await resultsRepo.UpdateAsync(result);
                        }
                    }

                    // Save manual checkpoint times
                    if (request.ManualCheckpointTimes?.Count > 0)
                    {
                        var checkpointRepo = _repository.GetRepository<Checkpoint>();
                        var splitTimesRepo = _repository.GetRepository<SplitTimes>();

                        // Load race checkpoints ordered by distance for segment calculation
                        var checkpoints = await checkpointRepo.GetQuery(c =>
                            c.RaceId == decryptedRaceId &&
                            c.AuditProperties.IsActive &&
                            !c.AuditProperties.IsDeleted)
                            .OrderBy(c => c.DistanceFromStart)
                            .AsNoTracking()
                            .ToListAsync();

                        // Get earliest normalized reading to use as start reference
                        var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                        var earliestReading = await normalizedRepo.GetQuery(r =>
                            r.ParticipantId == decryptedParticipantId &&
                            r.EventId == participant.EventId &&
                            r.AuditProperties.IsActive &&
                            !r.AuditProperties.IsDeleted)
                            .AsNoTracking()
                            .OrderBy(r => r.ChipTime)
                            .FirstOrDefaultAsync();

                        var raceStartTime = earliestReading?.ChipTime;

                        foreach (var ct in request.ManualCheckpointTimes)
                        {
                            var checkpointId = Convert.ToInt32(_encryptionService.Decrypt(ct.CheckpointId));
                            var checkpoint = checkpoints.FirstOrDefault(c => c.Id == checkpointId);
                            if (checkpoint == null) continue;

                            // Soft-delete any existing SplitTimes for this participant at this checkpoint
                            var existingSplits = await splitTimesRepo.GetQuery(st =>
                                st.ParticipantId == decryptedParticipantId &&
                                (st.CheckpointId == checkpointId || st.ToCheckpointId == checkpointId) &&
                                st.AuditProperties.IsActive &&
                                !st.AuditProperties.IsDeleted)
                                .ToListAsync();

                            foreach (var existing in existingSplits)
                            {
                                existing.AuditProperties.IsDeleted = true;
                                existing.AuditProperties.IsActive = false;
                                existing.AuditProperties.UpdatedBy = userId;
                                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                            }
                            if (existingSplits.Count > 0)
                                await splitTimesRepo.UpdateRangeAsync(existingSplits);

                            // Determine from/to checkpoint for segment
                            var checkpointIndex = checkpoints.IndexOf(checkpoint);
                            var fromCheckpoint = checkpointIndex > 0 ? checkpoints[checkpointIndex - 1] : checkpoint;

                            // Compute elapsed time from race start
                            long splitTimeMs = raceStartTime.HasValue
                                ? (long)(ct.Time - raceStartTime.Value).TotalMilliseconds
                                : 0;
                            var splitTimeSpan = splitTimeMs > 0
                                ? TimeSpan.FromMilliseconds(splitTimeMs)
                                : ct.Time.TimeOfDay;

                            await splitTimesRepo.AddAsync(new SplitTimes
                            {
                                EventId = participant.EventId,
                                ParticipantId = decryptedParticipantId,
                                FromCheckpointId = fromCheckpoint.Id,
                                ToCheckpointId = checkpointId,
                                CheckpointId = checkpointId,
                                SplitTime = splitTimeSpan,
                                SplitTimeMs = splitTimeMs > 0 ? splitTimeMs : null,
                                AuditProperties = new Models.Data.Common.AuditProperties
                                {
                                    CreatedBy = userId,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                }
                            });
                        }

                        participant.IsManualTiming = true;
                        await participantRepo.UpdateAsync(participant);
                        _logger.LogInformation("Saved {Count} manual checkpoint times for Participant {ParticipantId}", request.ManualCheckpointTimes.Count, participantId);
                    }
                });

                // #5: a DSQ re-ranks the race IN MEMORY (RankCalculator loads Status=="Finished"
                // only, so the DQ row drops out and everyone below steps up — overall, gender AND
                // category). UN-DSQ is the mirror image: the restored Finished runner re-enters
                // the set and everyone below steps back down. Deliberately NOT an RFID reprocess.
                if (dsqApplied || dsqCleared)
                {
                    await RankCalculator.ApplyStoredRanksAsync(
                        _repository, participant.EventId, decryptedRaceId, userId);
                    _logger.LogInformation(
                        dsqApplied
                            ? "Participant {ParticipantId} disqualified — race {RaceId} re-ranked (DSQ excluded)."
                            : "Participant {ParticipantId} DSQ cleared (status recomputed from gate coverage) — race {RaceId} re-ranked.",
                        participantId, decryptedRaceId);
                }

                _logger.LogInformation("Participant {ParticipantId} updated successfully by User {UserId}", participantId, userId);
                var searchResponse = MapToSearchResponse(participant);

                // Commit-f contract: a status-changing edit (DSQ / un-DSQ) returns the COMPLETE
                // post-recompute result — stored times, post-re-rank ranks, DISPLAY status and
                // TotalFinishers — reloaded AFTER the re-rank, so the UI can re-render without a
                // second fetch. Ranks are null when the runner is unranked (DNF/DNS/DSQ).
                if (dsqApplied || dsqCleared)
                {
                    var snapshotRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                    var updatedResult = await snapshotRepo.GetQuery(r =>
                        r.ParticipantId == decryptedParticipantId &&
                        r.EventId == participant.EventId &&
                        r.RaceId == decryptedRaceId &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    searchResponse.GunTime = TimeFormatter.FormatTimeSpan(updatedResult?.GunTime);
                    searchResponse.NetTime = TimeFormatter.FormatTimeSpan(updatedResult?.NetTime);
                    searchResponse.OverallRank = updatedResult?.OverallRank;
                    searchResponse.GenderRank = updatedResult?.GenderRank;
                    searchResponse.CategoryRank = updatedResult?.CategoryRank;
                    searchResponse.TotalFinishers = await snapshotRepo.CountAsync(r =>
                        r.RaceId == decryptedRaceId &&
                        r.Status == ResultStatus.Finished &&
                        r.AuditProperties.IsActive &&
                        !r.AuditProperties.IsDeleted);
                    searchResponse.Status = ResultStatus.ToDisplay(updatedResult?.Status ?? participant.Status);
                }

                return searchResponse;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating participant: {ex.Message}";
                _logger.LogError(ex, "Error in UpdateParticipantExtendedAsync for participant {ParticipantId}", participantId);
                return null;
            }
        }

        /// <summary>
        /// BUG-06: Moves a participant to a different race within the same event, migrating ALL
        /// timing data — Results, SplitTimes (soft-deleted, rebuilt by reprocess), ReadNormalized
        /// (reassigned + CheckpointId remapped to the target race's equivalent checkpoint by
        /// DistanceFromStart; readings with no equivalent are soft-deleted) and ChipAssignment —
        /// then reprocesses the target race (rebuild splits + recompute mandatory-aware status/ranks).
        /// The source participant is soft-deleted and a new one is created in the target race.
        ///
        /// Call with the source participant already loaded and any scalar field edits already applied
        /// to it (the new participant copies its fields from the source entity). Returns the new
        /// participant, or null if the target race is invalid (ErrorMessage is set).
        ///
        /// Shared by UpdateParticipantExtendedAsync and EditParticipant so both edit paths migrate
        /// timing data identically — do not duplicate this logic.
        /// </summary>
        // Moves a participant's REGISTRATION to a different race. This is a runtime race-move,
        // NOT an EF schema migration (the old "Migrate*" name violated the no-migrations naming
        // convention and misled debugging).
        //
        // CLEAN MODEL (derived data is never carried across races):
        //   A participant's timing data is physically bound to the race it was recorded on —
        //   ReadNormalized.CheckpointId → race-specific checkpoints, with race-relative Gun/Net
        //   times; SplitTimes/Results are derived from those. A 21.1K finisher's detections at
        //   the 5/10/15/21 km mats are meaningless on a 5K and cannot be reassigned. So we move
        //   the registration (same Participant row) and HARD-DELETE the derived + normalized
        //   rows. The immutable raw detections (ReadRaw) are RETAINED as the audit trail
        //   (ReadNormalized.RawReadId → ReadRaw is OnDelete:SetNull, so deleting ReadNormalized
        //   never touches ReadRaw). The participant lands in the target as "Registered"
        //   (effectively DNS until Process Result / a manual time produces target-race timing).
        //   The TARGET race is intentionally NOT rebuilt here — Process Result owns that, in its
        //   own clean context.
        private async Task<Models.Data.Entities.Participant?> MoveParticipantToRaceAsync(
            Models.Data.Entities.Participant participant,
            int targetRaceId)
        {
            var userId = _userContext.UserId;
            var sourceRaceId = participant.RaceId;
            var participantId = participant.Id;
            var eventId = participant.EventId;

            var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

            // Validate the target race belongs to this event.
            var raceRepo = _repository.GetRepository<Race>();
            var targetRaceExists = await raceRepo.GetQuery(r =>
                r.Id == targetRaceId &&
                r.EventId == eventId &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .AsNoTracking()
                .AnyAsync();

            if (!targetRaceExists)
            {
                ErrorMessage = "Target race not found or does not belong to this event";
                return null;
            }

            await _repository.ExecuteInTransactionAsync(async () =>
            {
                // 1. Move the registration on the SAME Participant row. Scalar/category edits
                //    were already applied by the caller; here we only change the race and reset
                //    run status (a moved runner is no longer a finisher in the target race).
                participant.RaceId = targetRaceId;
                participant.Status = "Registered";
                participant.AuditProperties.UpdatedBy = userId;
                participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                await participantRepo.UpdateAsync(participant);

                // 2. Make THIS participant's physical reads re-eligible for the target race's
                //    reprocess. The raw physical detections (RawRFIDReading) are RETAINED — they
                //    are the source the target reprocess re-projects onto the target race's
                //    checkpoints. (NOTE: ReadRaw is a dead table — nothing writes it; RawRFIDReading
                //    is the live raw layer.) Scope every operation to THIS participant's reads via
                //    their chip EPC(s): an EPC maps to exactly one participant per event
                //    (ChipAssignment), and we start from this participant's ChipAssignment rows, so
                //    no other participant's reads are touched.
                var chipAssignmentRepo = _repository.GetRepository<ChipAssignment>();
                var participantEpcs = await chipAssignmentRepo.GetQuery(ca =>
                        ca.ParticipantId == participantId &&
                        ca.EventId == eventId &&
                        !ca.UnassignedAt.HasValue &&
                        ca.AuditProperties.IsActive &&
                        !ca.AuditProperties.IsDeleted)
                    .Select(ca => ca.Chip.EPC)
                    .ToListAsync();

                if (participantEpcs.Count > 0)
                {
                    var batchRepo = _repository.GetRepository<UploadBatch>();
                    var eventBatchIds = await batchRepo.GetQuery(b => b.EventId == eventId)
                        .Select(b => b.Id)
                        .ToListAsync();

                    var rawReadingRepo = _repository.GetRepository<RawRFIDReading>();
                    var participantReadings = await rawReadingRepo.GetQuery(r =>
                            eventBatchIds.Contains(r.BatchId) &&
                            participantEpcs.Contains(r.Epc) &&
                            r.AuditProperties.IsActive &&
                            !r.AuditProperties.IsDeleted)
                        .ToListAsync();

                    if (participantReadings.Count > 0)
                    {
                        // 2a. Reset reads to Pending so Phase 1 of the target reprocess RELOADS and
                        //     re-assigns them to the target race's checkpoints. This is what makes a
                        //     move into a SIMPLE linear target race produce a real result instead of
                        //     a false DNS: Phase 1 loads ProcessResult=="Pending" only, and Phase 1.5
                        //     (which works on already-"Success" reads) skips simple races entirely.
                        //
                        //     DEPENDENCY (must stay true): this works because EVENT-LEVEL uploads keep
                        //     their batches perpetually "uploaded" (set at parse time, never marked
                        //     "completed"), so Phase 1's batch gate always includes them and a
                        //     per-reading Pending reset suffices — no batch flip needed. If a future
                        //     event uses RACE-LEVEL uploads (those batches DO get marked "completed",
                        //     and ClearProcessedData never resets event-level batches), this
                        //     reset-to-Pending path must be revisited. See .claude/CONTEXT.md.
                        foreach (var reading in participantReadings)
                        {
                            reading.ProcessResult = "Pending";
                            reading.ProcessedAt = null;
                            reading.AssignmentMethod = null;
                            reading.Notes = null;
                        }
                        await rawReadingRepo.BulkUpdateAsync(participantReadings);

                        // 2b. Hard-delete this participant's checkpoint assignments (gate c). Phase 1
                        //     SKIPS any reading that already has an active assignment, so without this
                        //     the reset reads would not be re-assigned; it also prevents Phase 2's
                        //     ToDictionary(ReadingId) from colliding on a stale source-race assignment.
                        //     Hard delete (BulkDelete) matches the ClearProcessedDataAsync precedent.
                        var participantReadingIds = participantReadings.Select(r => r.Id).ToList();
                        var assignmentRepo = _repository.GetRepository<ReadingCheckpointAssignment>();
                        var oldAssignments = await assignmentRepo.GetQuery(a =>
                                participantReadingIds.Contains(a.ReadingId))
                            .ToListAsync();
                        if (oldAssignments.Count > 0)
                            await assignmentRepo.BulkDeleteAsync(oldAssignments);
                    }
                }

                // 3. HARD-DELETE this participant's race-bound derived + normalized rows — never
                //    carried across races; the target reprocess rebuilds them from the retained raw.
                //    Bulk delete (immediate SQL, tracker-bypassing) matches the ClearProcessedDataAsync
                //    precedent.
                var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();
                var oldResults = await resultsRepo.GetQuery(r => r.ParticipantId == participantId).ToListAsync();
                if (oldResults.Count > 0)
                    await resultsRepo.BulkDeleteAsync(oldResults);

                var splitTimesRepo = _repository.GetRepository<SplitTimes>();
                var oldSplits = await splitTimesRepo.GetQuery(st => st.ParticipantId == participantId).ToListAsync();
                if (oldSplits.Count > 0)
                    await splitTimesRepo.BulkDeleteAsync(oldSplits);

                var normalizedRepo = _repository.GetRepository<ReadNormalized>();
                var oldNormalized = await normalizedRepo.GetQuery(rn => rn.ParticipantId == participantId).ToListAsync();
                if (oldNormalized.Count > 0)
                    await normalizedRepo.BulkDeleteAsync(oldNormalized);

                // 3b. Soft-delete this participant's manual overrides. A ManualTimeOverride is keyed to a
                //     SOURCE-race CheckpointId, which is meaningless in the target race — left active, the
                //     target reprocess (Phase 2.4) would re-inject a stale-checkpoint override and corrupt
                //     the result. Soft-delete (not hard) to keep the audit trail; the runner re-times in
                //     the target race from raw. Bulk update matches the surrounding bulk operations.
                var overrideRepo = _repository.GetRepository<ManualTimeOverride>();
                var oldOverrides = await overrideRepo.GetQuery(o =>
                        o.ParticipantId == participantId &&
                        !o.AuditProperties.IsDeleted)
                    .ToListAsync();
                if (oldOverrides.Count > 0)
                {
                    foreach (var ov in oldOverrides)
                    {
                        ov.AuditProperties.IsDeleted = true;
                        ov.AuditProperties.IsActive = false;
                        ov.AuditProperties.UpdatedBy = userId;
                        ov.AuditProperties.UpdatedDate = DateTime.UtcNow;
                    }
                    await overrideRepo.BulkUpdateAsync(oldOverrides);
                }

                // 4. ChipAssignment stays with the participant (same row, physical chip) — no
                //    reassignment needed since the participant Id is unchanged.

                // 5. Persist the registration move, then re-rank the SOURCE race. The participant's
                //    Results were just deleted, so the source re-rank naturally excludes them
                //    (the gap is closed). Re-rank writes via bulk update (tracker-bypassing) — no
                //    Include/cascade, no NoTracking double-attach.
                await _repository.SaveChangesAsync();
                await ReRankRaceAsync(eventId, sourceRaceId, userId);
            });

            _logger.LogInformation(
                "Participant {ParticipantId} moved from Race {OldRace} to Race {NewRace}; raw reads reset to Pending + checkpoint assignments cleared, derived timing (Results/SplitTimes/ReadNormalized) hard-deleted, raw detections (RawRFIDReading) retained, source race re-ranked. Target race timing rebuilt + ranked on Process Result.",
                participantId, sourceRaceId, targetRaceId);
            return participant;
        }

        // Re-ranks a race's finishers (Overall / Gender / Category) and persists via bulk update.
        // Deliberately does NOT Include(r => r.Participant): under the global NoTracking default,
        // Include + UpdateRange would attach the Participant graph as Modified (cascade) and risk
        // duplicate-instance / phantom-PK writes. Instead we write Results via BulkUpdateAsync
        // (tracker-bypassing) and resolve gender/category from a lightweight projection.
        private async Task ReRankRaceAsync(int eventId, int raceId, int userId)
        {
            var resultsRepo = _repository.GetRepository<Models.Data.Entities.Results>();

            var results = await resultsRepo.GetQuery(r =>
                r.EventId == eventId &&
                r.RaceId == raceId &&
                r.Status == "Finished" &&
                r.FinishTime.HasValue &&
                r.AuditProperties.IsActive &&
                !r.AuditProperties.IsDeleted)
                .OrderBy(r => r.FinishTime)
                .ToListAsync();

            if (results.Count == 0)
                return;

            // Project participant gender/category for these finishers (no navigation cascade).
            var participantIds = results.Select(r => r.ParticipantId).Distinct().ToList();
            var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();
            var participantInfo = await participantRepo.GetQuery(p => participantIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Gender, p.AgeCategory })
                .ToListAsync();
            var genderById = participantInfo.ToDictionary(p => p.Id, p => p.Gender);
            var categoryById = participantInfo.ToDictionary(p => p.Id, p => p.AgeCategory);

            var rank = 1;
            foreach (var result in results)
            {
                result.OverallRank = rank++;
                result.AuditProperties.UpdatedBy = userId;
                result.AuditProperties.UpdatedDate = DateTime.UtcNow;
            }

            foreach (var gender in new[] { "M", "F" })
            {
                rank = 1;
                foreach (var result in results.Where(r => genderById.GetValueOrDefault(r.ParticipantId) == gender))
                    result.GenderRank = rank++;
            }

            var categories = results
                .Select(r => categoryById.GetValueOrDefault(r.ParticipantId))
                .Distinct()
                .Where(c => !string.IsNullOrEmpty(c));

            foreach (var category in categories)
            {
                rank = 1;
                foreach (var result in results.Where(r => categoryById.GetValueOrDefault(r.ParticipantId) == category))
                    result.CategoryRank = rank++;
            }

            await resultsRepo.BulkUpdateAsync(results);
        }

        private ParticipantSearchReponse MapToSearchResponse(Models.Data.Entities.Participant p)
        {
            return new ParticipantSearchReponse
            {
                Id = _encryptionService.Encrypt(p.Id.ToString()),
                Bib = p.BibNumber,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                Phone = p.Phone,
                Gender = string.IsNullOrWhiteSpace(p.Gender) ? "Unknown" : p.Gender,
                Category = string.IsNullOrWhiteSpace(p.AgeCategory) ? "Unknown" : p.AgeCategory,
                Status = p.Status
            };
        }

        public async Task DeleteParticipantAsync(string raceId, string participantId)
        {
            try
            {
                var decryptedRaceId = Convert.ToInt32(_encryptionService.Decrypt(raceId));
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));
                var tenantId = _userContext.TenantId;

                var participantRepo = _repository.GetRepository<Models.Data.Entities.Participant>();

                var participant = await participantRepo.GetQuery(p =>
                    p.Id == decryptedParticipantId &&
                    p.RaceId == decryptedRaceId &&
                    p.TenantId == tenantId &&
                    p.AuditProperties.IsActive &&
                    !p.AuditProperties.IsDeleted)
                    .FirstOrDefaultAsync();

                if (participant == null)
                {
                    ErrorMessage = "Participant not found";
                    _logger.LogWarning("DeleteParticipant failed: Participant {ParticipantId} not found in Race {RaceId}", participantId, raceId);
                    return;
                }

                participant.AuditProperties.IsActive = false;
                participant.AuditProperties.IsDeleted = true;
                participant.AuditProperties.UpdatedDate = DateTime.UtcNow;
                participant.AuditProperties.UpdatedBy = _userContext.UserId;

                await _repository.ExecuteInTransactionAsync(async () =>
                {
                    await participantRepo.UpdateAsync(participant);
                });

                _logger.LogInformation("Participant {ParticipantId} soft-deleted from Race {RaceId}", participantId, raceId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error deleting participant: {ex.Message}";
                _logger.LogError(ex, "Error in DeleteParticipantAsync for participant {ParticipantId}", participantId);
            }
        }

        public async Task<ParticipantDetectionsResponse?> GetDetectionsAsync(
            string eventId,
            string raceId,
            string participantId,
            string? checkpointId,
            CancellationToken ct)
        {
            try
            {
                var decryptedParticipantId = Convert.ToInt32(_encryptionService.Decrypt(participantId));

                var participant = await _repository.GetRepository<Models.Data.Entities.Participant>()
                    .GetQuery(p => p.Id == decryptedParticipantId && !p.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (participant == null)
                {
                    ErrorMessage = "Participant not found.";
                    return null;
                }

                int? filterCheckpointId = null;
                if (!string.IsNullOrEmpty(checkpointId))
                    filterCheckpointId = Convert.ToInt32(_encryptionService.Decrypt(checkpointId));

                // Get participant's active chip EPC(s)
                var chipEpcs = await _repository.GetRepository<ChipAssignment>()
                    .GetQuery(ca => ca.ParticipantId == decryptedParticipantId && !ca.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(ca => ca.Chip.EPC)
                    .ToListAsync(ct);

                // Single query: RawRFIDReading → ReadingCheckpointAssignment → Checkpoint → Device
                var readingsQuery = _repository.GetRepository<RawRFIDReading>()
                    .GetQuery(r => chipEpcs.Contains(r.Epc) && !r.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Select(r => new
                    {
                        r.Id,
                        r.ReadTimeUtc,
                        r.DeviceId,
                        r.RssiDbm,
                        r.ProcessResult,
                        r.ManualTimeOverride,
                        r.IsManualEntry,
                        r.Notes,
                        Assignment = r.ReadingCheckpointAssignments
                            .Where(a => !a.AuditProperties.IsDeleted
                                && (filterCheckpointId == null || a.CheckpointId == filterCheckpointId))
                            .Select(a => new
                            {
                                a.CheckpointId,
                                CheckpointName = a.Checkpoint.Name ?? string.Empty,
                                a.Checkpoint.IsMandatory,
                                DeviceName = a.Checkpoint.Device != null ? a.Checkpoint.Device.Name : string.Empty
                            })
                            .FirstOrDefault()
                    });

                // If filtering by checkpoint, only include readings that have that assignment
                if (filterCheckpointId.HasValue)
                    readingsQuery = readingsQuery.Where(r => r.Assignment != null);

                var readings = await readingsQuery
                    .OrderBy(r => r.ReadTimeUtc)
                    .ToListAsync(ct);

                // Group by checkpoint
                var grouped = readings
                    .GroupBy(r => r.Assignment == null
                        ? 0
                        : r.Assignment.CheckpointId)
                    .Select(g =>
                    {
                        var first = g.FirstOrDefault(x => x.Assignment != null);
                        return new CheckpointDetectionGroupDto
                        {
                            CheckpointId = g.Key == 0 ? string.Empty : _encryptionService.Encrypt(g.Key.ToString()),
                            CheckpointName = first?.Assignment?.CheckpointName ?? "Unassigned",
                            IsMandatory = first?.Assignment?.IsMandatory ?? false,
                            Detections = g.Select(r => new DetectionRowDto
                            {
                                ReadingId = _encryptionService.Encrypt(r.Id.ToString()),
                                ReadTimeUtc = r.ReadTimeUtc,
                                DeviceId = r.DeviceId,
                                ReaderName = r.Assignment?.DeviceName ?? string.Empty,
                                RssiDbm = r.RssiDbm.HasValue ? (int?)Convert.ToInt32(r.RssiDbm.Value) : null,
                                ProcessResult = r.ProcessResult,
                                ManualTime = r.ManualTimeOverride.HasValue
                                    ? (TimeSpan?)r.ManualTimeOverride.Value.TimeOfDay
                                    : null,
                                IsManualEntry = r.IsManualEntry,
                                Notes = r.Notes
                            }).ToList()
                        };
                    })
                    .Where(g => g.CheckpointId != string.Empty) // exclude unassigned unless filtering
                    .ToList();

                return new ParticipantDetectionsResponse
                {
                    ParticipantId = _encryptionService.Encrypt(participant.Id.ToString()),
                    Bib = participant.BibNumber ?? string.Empty,
                    FullName = $"{participant.FirstName} {participant.LastName}".Trim(),
                    Gender = participant.Gender ?? string.Empty,
                    ManualDistance = participant.ManualDistance,
                    Checkpoints = grouped
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading detections: {ex.Message}";
                _logger.LogError(ex, "Error in GetDetectionsAsync for participant {ParticipantId}", participantId);
                return null;
            }
        }
    }
}