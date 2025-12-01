using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Common;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;
using System.Globalization;
using System.Text;

namespace Runnatics.Services
{
    public class ParticipantImportService(
        IUnitOfWork<RaceSyncDbContext> repository,
        ILogger<ParticipantImportService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IParticipantImportService
    {
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
                    AuditProperties = new AuditProperties
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
                    record.AuditProperties = new AuditProperties
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
                /*
                // Process each staging record
                foreach (var staging in stagingRecords)
                {
                    try
                    {
                        // Parse name
                        var (firstName, lastName) = ParseName(staging.FirstName);

                        // Clean phone number
                        var cleanedPhone = CleanPhoneNumber(staging.Mobile);

                        // Check for duplicate BIB
                        if (!string.IsNullOrWhiteSpace(staging.Bib))
                        {
                            var existingBib = await participantRepo.GetQuery(p =>
                                p.EventId == decryptedEventId &&
                                p.BibNumber == staging.Bib &&
                                !p.AuditProperties.IsDeleted)
                                .AsNoTracking()
                                .AnyAsync();

                            if (existingBib)
                            {
                                staging.ProcessingStatus = "Error";
                                staging.ErrorMessage = $"BIB number {staging.Bib} already exists for this event";
                                errorCount++;

                                response.Errors.Add(new ProcessingError
                                {
                                    StagingId = staging.Id,
                                    RowNumber = staging.RowNumber,
                                    Bib = staging.Bib ?? "",
                                    Name = staging.FirstName ?? "",
                                    ErrorMessage = staging.ErrorMessage
                                });

                                continue;
                            }
                        }

                        // Determine race - use provided raceId or try to get default race for event
                        int finalRaceId = raceId ?? await GetDefaultRaceForEventAsync(decryptedEventId);

                        if (finalRaceId == 0)
                        {
                            staging.ProcessingStatus = "Error";
                            staging.ErrorMessage = "No race specified and no default race found for event";
                            errorCount++;

                            response.Errors.Add(new ProcessingError
                            {
                                StagingId = staging.Id,
                                RowNumber = staging.RowNumber,
                                Bib = staging.Bib ?? "",
                                Name = staging.FirstName ?? "",
                                ErrorMessage = staging.ErrorMessage
                            });

                            continue;
                        }

                        // Create participant
                        var participant = new Models.Data.Entities.Participant
                        {
                            TenantId = tenantId,
                            EventId = decryptedEventId,
                            RaceId = finalRaceId,
                            ImportBatchId = decryptedImportBatchId,
                            BibNumber = staging.Bib,
                            FirstName = firstName,
                            LastName = lastName,
                            Email = staging.Email,
                            Phone = cleanedPhone,
                            Gender = NormalizeGender(staging.Gender),
                            AgeCategory = staging.AgeCategory,
                            Status = "Registered",
                            RegistrationDate = DateTime.UtcNow,
                            AuditProperties = new AuditProperties
                            {
                                CreatedBy = userId,
                                CreatedDate = DateTime.UtcNow,
                                IsActive = true,
                                IsDeleted = false
                            }
                        };

                        await participantRepo.AddAsync(participant);
                        await _repository.SaveChangesAsync();

                        // Update staging record
                        staging.ProcessingStatus = "Success";
                        staging.ParticipantId = participant.Id;
                        staging.AuditProperties.UpdatedBy = userId;
                        staging.AuditProperties.UpdatedDate = DateTime.UtcNow;

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        staging.ProcessingStatus = "Error";
                        staging.ErrorMessage = ex.Message;
                        staging.AuditProperties.UpdatedBy = userId;
                        staging.AuditProperties.UpdatedDate = DateTime.UtcNow;
                        errorCount++;

                        response.Errors.Add(new ProcessingError
                        {
                            StagingId = staging.Id,
                            RowNumber = staging.RowNumber,
                            Bib = staging.Bib ?? "",
                            Name = staging.FirstName ?? "",
                            ErrorMessage = ex.Message
                        });

                        _logger.LogError(ex, "Error processing staging record {StagingId}", staging.Id);
                    }
                }

                // Update staging records
                await _repository.SaveChangesAsync();
                */
               var batchProcessor = stagingRepo.ExecuteStoredProcedure<object>(
                    "sp_ProcessParticipantImportBatch",
                    new { ImportBatchId = decryptedImportBatchId, EventId = decryptedEventId }
                );

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

            // if (string.IsNullOrWhiteSpace(record.FirstName))
            // {
            //     errors.Add(new ValidationError
            //     {
            //         RowNumber = record.RowNumber,
            //         Field = "Name",
            //         Message = "Name is required",
            //         Value = record.FirstName ?? ""
            //     });
            // }

            return errors;
        }

        private (string firstName, string lastName) ParseName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return ("Unknown", "");

            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return ("Unknown", "");

            if (parts.Length == 1)
                return (parts[0], "");

            var firstName = parts[0];
            var lastName = string.Join(" ", parts.Skip(1));

            return (firstName, lastName);
        }

        private string? CleanPhoneNumber(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null;

            return new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        }

        private string? NormalizeGender(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender))
                return null;

            var normalized = gender.Trim().ToLower();

            if (normalized.StartsWith("m") || normalized == "male")
                return "Male";

            if (normalized.StartsWith("f") || normalized == "female")
                return "Female";

            return "Other";
        }

        private async Task<int> GetDefaultRaceForEventAsync(int eventId)
        {
            try
            {
                var raceRepo = _repository.GetRepository<Race>();
                var race = await raceRepo.GetQuery(r =>
                    r.EventId == eventId &&
                    r.AuditProperties.IsActive &&
                    !r.AuditProperties.IsDeleted)
                    .OrderBy(r => r.AuditProperties.CreatedDate)
                    .FirstOrDefaultAsync();

                return race?.Id ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
