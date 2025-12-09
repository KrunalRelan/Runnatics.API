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