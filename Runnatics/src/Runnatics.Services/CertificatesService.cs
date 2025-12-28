using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Certificates;
using Runnatics.Models.Client.Responses.Certificates;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class CertificatesService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        ILogger<CertificatesService> logger,
        IUserContextService userContext,
        IEncryptionService encryptionService) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), ICertificatesService
    {
        private readonly IMapper _mapper = mapper;
        private readonly ILogger<CertificatesService> _logger = logger;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;

        private static Expression<Func<CertificateTemplate, bool>> IsActiveFilter =>
            t => t.AuditProperties.IsActive && !t.AuditProperties.IsDeleted;

        public async Task<CertificateTemplateResponse?> CreateTemplateAsync(CertificateTemplateRequest request)
        {
            try
            {
                var decryptedEventId = TryParseOrDecrypt(request.EventId, nameof(request.EventId));
                int? decryptedRaceId = null;

                if (!string.IsNullOrEmpty(request.RaceId))
                {
                    decryptedRaceId = TryParseOrDecrypt(request.RaceId, nameof(request.RaceId));
                }

                if (!await EventExistsAsync(decryptedEventId))
                {
                    ErrorMessage = "Event not found or inactive.";
                    return null;
                }

                if (decryptedRaceId.HasValue && !await RaceExistsAsync(decryptedEventId, decryptedRaceId.Value))
                {
                    ErrorMessage = "Race not found or inactive.";
                    return null;
                }

                var currentUserId = GetCurrentUserId();
                var templateRepo = _repository.GetRepository<CertificateTemplate>();

                var template = new CertificateTemplate
                {
                    EventId = decryptedEventId,
                    RaceId = decryptedRaceId,
                    Name = request.Name,
                    Description = request.Description,
                    Width = request.Width,
                    Height = request.Height,
                    //IsActive = request.IsActive,
                    AuditProperties = CreateAuditProperties(currentUserId)
                };

                if (!string.IsNullOrEmpty(request.BackgroundImageData))
                {
                    template.BackgroundImageUrl = await SaveBackgroundImageAsync(request.BackgroundImageData);
                }

                await templateRepo.AddAsync(template);
                await _repository.SaveChangesAsync();

                if (request.Fields.Any())
                {
                    var fieldRepo = _repository.GetRepository<CertificateField>();
                    var fields = request.Fields.Select(f => new CertificateField
                    {
                        TemplateId = template.Id,
                        FieldType = f.FieldType,
                        Content = f.Content,
                        XCoordinate = f.XCoordinate,
                        YCoordinate = f.YCoordinate,
                        Font = f.Font,
                        FontSize = f.FontSize,
                        FontColor = f.FontColor,
                        Width = f.Width,
                        Height = f.Height,
                        Alignment = f.Alignment ?? "left",
                        FontWeight = f.FontWeight ?? "normal",
                        FontStyle = f.FontStyle ?? "normal",
                        AuditProperties = CreateAuditProperties(currentUserId)
                    }).ToList();

                    await fieldRepo.AddRangeAsync(fields);
                    await _repository.SaveChangesAsync();
                }

                _logger.LogInformation("Certificate template created. Id: {Id}, EventId: {EventId}, CreatedBy: {UserId}",
                    template.Id, template.EventId, currentUserId);

                return await GetTemplateAsync(_encryptionService.Encrypt(template.Id.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating certificate template for EventId: {EventId}", request.EventId);
                ErrorMessage = "Error creating certificate template.";
                return null;
            }
        }

        public async Task<CertificateTemplateResponse?> UpdateTemplateAsync(string id, CertificateTemplateRequest request)
        {
            try
            {
                var decryptedId = TryParseOrDecrypt(id, nameof(id));
                var decryptedEventId = TryParseOrDecrypt(request.EventId, nameof(request.EventId));
                int? decryptedRaceId = null;

                if (!string.IsNullOrEmpty(request.RaceId))
                {
                    decryptedRaceId = TryParseOrDecrypt(request.RaceId, nameof(request.RaceId));
                }

                var templateRepo = _repository.GetRepository<CertificateTemplate>();
                var existing = await templateRepo
                    .GetQuery(t => t.Id == decryptedId)
                    .Where(IsActiveFilter)
                    .Include(t => t.Fields)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    ErrorMessage = "Certificate template not found.";
                    _logger.LogWarning("Certificate template not found: {Id}", id);
                    return null;
                }

                if (!await EventExistsAsync(decryptedEventId))
                {
                    ErrorMessage = "Event not found or inactive.";
                    return null;
                }

                if (decryptedRaceId.HasValue && !await RaceExistsAsync(decryptedEventId, decryptedRaceId.Value))
                {
                    ErrorMessage = "Race not found or inactive.";
                    return null;
                }

                var currentUserId = GetCurrentUserId();

                existing.EventId = decryptedEventId;
                existing.RaceId = decryptedRaceId;
                existing.Name = request.Name;
                existing.Description = request.Description;
                existing.Width = request.Width;
                existing.Height = request.Height;
                //existing.IsActive = request.IsActive;
                existing.AuditProperties.UpdatedDate = DateTime.UtcNow;
                existing.AuditProperties.UpdatedBy = currentUserId;

                if (!string.IsNullOrEmpty(request.BackgroundImageData))
                {
                    existing.BackgroundImageUrl = await SaveBackgroundImageAsync(request.BackgroundImageData);
                }

                var fieldRepo = _repository.GetRepository<CertificateField>();
                var fieldIdsToDelete = existing.Fields.Select(f => f.Id).ToList();
                foreach (var fieldId in fieldIdsToDelete)
                {
                    await fieldRepo.DeleteAsync(fieldId);
                }

                var newFields = request.Fields.Select(f => new CertificateField
                {
                    TemplateId = existing.Id,
                    FieldType = f.FieldType,
                    Content = f.Content,
                    XCoordinate = f.XCoordinate,
                    YCoordinate = f.YCoordinate,
                    Font = f.Font,
                    FontSize = f.FontSize,
                    FontColor = f.FontColor,
                    Width = f.Width,
                    Height = f.Height,
                    Alignment = f.Alignment ?? "left",
                    FontWeight = f.FontWeight ?? "normal",
                    FontStyle = f.FontStyle ?? "normal"
                }).ToList();

                await fieldRepo.AddRangeAsync(newFields);
                await templateRepo.UpdateAsync(existing);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Certificate template updated. Id: {Id}", decryptedId);

                return await GetTemplateAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating certificate template: {Id}", id);
                ErrorMessage = "Error updating certificate template.";
                return null;
            }
        }

        public async Task<CertificateTemplateResponse?> GetTemplateAsync(string id)
        {
            try
            {
                var decryptedId = TryParseOrDecrypt(id, nameof(id));
                var templateRepo = _repository.GetRepository<CertificateTemplate>();

                var template = await templateRepo
                    .GetQuery(t => t.Id == decryptedId)
                    .Where(IsActiveFilter)
                    .Include(t => t.Fields)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (template == null)
                {
                    ErrorMessage = "Certificate template not found.";
                    return null;
                }

                return MapToResponse(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving certificate template: {Id}", id);
                ErrorMessage = "Error retrieving certificate template.";
                return null;
            }
        }

        public async Task<List<CertificateTemplateResponse>> GetTemplatesByEventAsync(string eventId)
        {
            try
            {
                var decryptedEventId = TryParseOrDecrypt(eventId, nameof(eventId));
                var templateRepo = _repository.GetRepository<CertificateTemplate>();

                var templates = await templateRepo
                    .GetQuery(t => t.EventId == decryptedEventId)
                    .Where(IsActiveFilter)
                    .Include(t => t.Fields)
                    .AsNoTracking()
                    .OrderBy(t => t.AuditProperties.CreatedDate)
                    .ToListAsync();

                return templates.Select(MapToResponse).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving certificate templates for EventId: {EventId}", eventId);
                ErrorMessage = "Error retrieving certificate templates.";
                return new List<CertificateTemplateResponse>();
            }
        }

        public async Task<CertificateTemplateResponse?> GetTemplateByRaceAsync(string eventId, string raceId)
        {
            try
            {
                var decryptedEventId = TryParseOrDecrypt(eventId, nameof(eventId));
                var decryptedRaceId = TryParseOrDecrypt(raceId, nameof(raceId));
                var templateRepo = _repository.GetRepository<CertificateTemplate>();

                var template = await templateRepo
                    .GetQuery(t => t.EventId == decryptedEventId && t.RaceId == decryptedRaceId)
                    .Where(IsActiveFilter)
                    .Include(t => t.Fields)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (template == null)
                {
                    var eventWideTemplate = await templateRepo
                        .GetQuery(t => t.EventId == decryptedEventId && t.RaceId == null)
                        .Where(IsActiveFilter)
                        .Include(t => t.Fields)
                        .AsNoTracking()
                        .FirstOrDefaultAsync();

                    if (eventWideTemplate == null)
                    {
                        ErrorMessage = "No certificate template found for this race or event.";
                        return null;
                    }

                    return MapToResponse(eventWideTemplate);
                }

                return MapToResponse(template);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving certificate template for Race: {RaceId}, Event: {EventId}", raceId, eventId);
                ErrorMessage = "Error retrieving certificate template.";
                return null;
            }
        }

        public async Task<bool> DeleteTemplateAsync(string id)
        {
            try
            {
                var decryptedId = TryParseOrDecrypt(id, nameof(id));
                var templateRepo = _repository.GetRepository<CertificateTemplate>();

                var template = await templateRepo
                    .GetQuery(t => t.Id == decryptedId)
                    .Where(IsActiveFilter)
                    .FirstOrDefaultAsync();

                if (template == null)
                {
                    ErrorMessage = "Certificate template not found.";
                    _logger.LogWarning("Certificate template delete failed - not found: {Id}", id);
                    return false;
                }

                template.AuditProperties.IsActive = false;
                template.AuditProperties.IsDeleted = true;
                template.AuditProperties.UpdatedDate = DateTime.UtcNow;
                template.AuditProperties.UpdatedBy = _userContext.UserId;

                await templateRepo.UpdateAsync(template);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Certificate template deleted. Id: {Id}", decryptedId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting certificate template: {Id}", id);
                ErrorMessage = "Error deleting certificate template.";
                return false;
            }
        }

        #region Helpers

        private int GetCurrentUserId() => _userContext?.IsAuthenticated == true ? _userContext.UserId : 0;

        private int TryParseOrDecrypt(string input, string inputName)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Id input cannot be null or empty", inputName);

            if (int.TryParse(input, out var id))
                return id;

            try
            {
                var decrypted = _encryptionService.Decrypt(input);
                if (int.TryParse(decrypted, out id))
                    return id;

                _logger.LogDebug("Decrypted value for {InputName} did not parse as int", inputName);
                throw new ArgumentException($"Invalid {inputName} format");
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogDebug(ex, "Failed to parse or decrypt input for {InputName}", inputName);
                throw new ArgumentException($"Invalid {inputName} format", inputName, ex);
            }
        }

        private async Task<bool> EventExistsAsync(int eventId)
        {
            var eventRepo = _repository.GetRepository<Event>();
            return await eventRepo
                .GetQuery(e => e.Id == eventId)
                .Where(e => e.AuditProperties.IsActive && !e.AuditProperties.IsDeleted)
                .AsNoTracking()
                .AnyAsync();
        }

        private async Task<bool> RaceExistsAsync(int eventId, int raceId)
        {
            var raceRepo = _repository.GetRepository<Race>();
            return await raceRepo
                .GetQuery(r => r.Id == raceId && r.EventId == eventId)
                .Where(r => r.AuditProperties.IsActive && !r.AuditProperties.IsDeleted)
                .AsNoTracking()
                .AnyAsync();
        }

        private async Task<string> SaveBackgroundImageAsync(string base64Data)
        {
            // TODO: Implement actual file storage (Azure Blob Storage, S3, etc.)
            // For now, just return a placeholder
            await Task.CompletedTask;
            return $"/certificates/backgrounds/{Guid.NewGuid()}.png";
        }

        private CertificateTemplateResponse MapToResponse(CertificateTemplate template)
        {
            return new CertificateTemplateResponse
            {
                Id = _encryptionService.Encrypt(template.Id.ToString()),
                EventId = _encryptionService.Encrypt(template.EventId.ToString()),
                RaceId = template.RaceId.HasValue ? _encryptionService.Encrypt(template.RaceId.Value.ToString()) : null,
                Name = template.Name,
                Description = template.Description,
                BackgroundImageUrl = template.BackgroundImageUrl,
                Width = template.Width,
                Height = template.Height,
                //IsActive = template.IsActive,
                CreatedAt = template.AuditProperties.CreatedDate,
                UpdatedAt = template.AuditProperties.UpdatedDate,
                Fields = template.Fields.Select(f => new CertificateFieldResponse
                {
                    Id = _encryptionService.Encrypt(f.Id.ToString()),
                    FieldType = f.FieldType,
                    Content = f.Content,
                    XCoordinate = f.XCoordinate,
                    YCoordinate = f.YCoordinate,
                    Font = f.Font,
                    FontSize = f.FontSize,
                    FontColor = f.FontColor,
                    Width = f.Width,
                    Height = f.Height,
                    Alignment = f.Alignment,
                    FontWeight = f.FontWeight,
                    FontStyle = f.FontStyle
                }).ToList()
            };
        }

        private static Models.Data.Common.AuditProperties CreateAuditProperties(int userId)
        {
            return new Models.Data.Common.AuditProperties
            {
                IsActive = true,
                IsDeleted = false,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = userId
            };
        }

        #endregion
    }
}
