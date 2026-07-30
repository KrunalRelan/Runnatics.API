using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Public;
using Runnatics.Models.Client.Requests.About;
using Runnatics.Models.Client.Responses.About;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class AboutContentService(
        IUnitOfWork<RaceSyncDbContext> repository,
        IMapper mapper,
        IUserContextService userContext,
        IEncryptionService encryptionService,
        ILogger<AboutContentService> logger) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), IAboutContentService
    {
        private readonly IMapper _mapper = mapper;
        private readonly IUserContextService _userContext = userContext;
        private readonly IEncryptionService _encryptionService = encryptionService;
        private readonly ILogger<AboutContentService> _logger = logger;

        // SiteContents keys owned by the About page.
        private const string KeyWhoWeAre = "About.WhoWeAre";
        private const string KeyMission = "About.Mission";
        private const string KeyStoryImage = "About.StoryImage";

        // ~1MB of binary as base64 (4/3 expansion). Client caps uploads at 500KB;
        // this is the server-side backstop so a bad client can't bloat the table.
        private const int MaxImageBase64Length = 1_400_000;

        #region Public read

        public async Task<PublicAboutDto> GetPublicAboutAsync(CancellationToken ct = default)
        {
            try
            {
                var texts = await LoadContentMapAsync(ct);
                var founders = await ActiveFoundersQuery()
                    .Select(f => new PublicFounderDto
                    {
                        Name = f.Name,
                        Role = f.Role,
                        Bio = f.Bio,
                        PhotoBase64 = f.PhotoBase64
                    })
                    .ToListAsync(ct);

                return new PublicAboutDto
                {
                    WhoWeAre = texts.GetValueOrDefault(KeyWhoWeAre),
                    Mission = texts.GetValueOrDefault(KeyMission),
                    StoryImageBase64 = texts.GetValueOrDefault(KeyStoryImage),
                    Founders = founders
                };
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error retrieving about content.";
                _logger.LogError(ex, "Error in GetPublicAboutAsync");
                return new PublicAboutDto();
            }
        }

        #endregion

        #region Admin read

        public async Task<AboutContentDto> GetAboutContentAsync(CancellationToken ct = default)
        {
            try
            {
                var texts = await LoadContentMapAsync(ct);
                var founders = await ActiveFoundersQuery().ToListAsync(ct);

                return new AboutContentDto
                {
                    WhoWeAre = texts.GetValueOrDefault(KeyWhoWeAre),
                    Mission = texts.GetValueOrDefault(KeyMission),
                    StoryImageBase64 = texts.GetValueOrDefault(KeyStoryImage),
                    Founders = _mapper.Map<List<FounderDto>>(founders)
                };
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error retrieving about content.";
                _logger.LogError(ex, "Error in GetAboutContentAsync");
                return new AboutContentDto();
            }
        }

        #endregion

        #region Admin write — copy

        public async Task<bool> UpdateAboutContentAsync(UpdateAboutContentRequest request, CancellationToken ct = default)
        {
            try
            {
                if (request.StoryImageBase64 != null && request.StoryImageBase64.Length > MaxImageBase64Length)
                {
                    this.ErrorMessage = "Story image is too large. Please upload an image under 1MB.";
                    return false;
                }

                await UpsertContentAsync(KeyWhoWeAre, request.WhoWeAre, ct);
                await UpsertContentAsync(KeyMission, request.Mission, ct);
                await UpsertContentAsync(KeyStoryImage, request.StoryImageBase64, ct);

                await _repository.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error saving about content.";
                _logger.LogError(ex, "Error in UpdateAboutContentAsync");
                return false;
            }
        }

        #endregion

        #region Admin write — founders

        public async Task<FounderDto?> CreateFounderAsync(SaveFounderRequest request, CancellationToken ct = default)
        {
            try
            {
                if (!ValidateFounder(request))
                    return null;

                var founder = new Founder
                {
                    Name = request.Name.Trim(),
                    Role = request.Role?.Trim(),
                    Bio = request.Bio?.Trim(),
                    PhotoBase64 = request.PhotoBase64,
                    DisplayOrder = request.DisplayOrder,
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        IsActive = true,
                        IsDeleted = false,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = _userContext.UserId
                    }
                };

                await _repository.GetRepository<Founder>().AddAsync(founder);
                await _repository.SaveChangesAsync();

                return _mapper.Map<FounderDto>(founder);
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error creating founder.";
                _logger.LogError(ex, "Error in CreateFounderAsync");
                return null;
            }
        }

        public async Task<FounderDto?> UpdateFounderAsync(string encryptedFounderId, SaveFounderRequest request, CancellationToken ct = default)
        {
            try
            {
                if (!ValidateFounder(request))
                    return null;

                var founder = await FindFounderAsync(encryptedFounderId, ct);
                if (founder == null)
                    return null;

                founder.Name = request.Name.Trim();
                founder.Role = request.Role?.Trim();
                founder.Bio = request.Bio?.Trim();
                founder.PhotoBase64 = request.PhotoBase64;
                founder.DisplayOrder = request.DisplayOrder;
                founder.AuditProperties.UpdatedDate = DateTime.UtcNow;
                founder.AuditProperties.UpdatedBy = _userContext.UserId;

                await _repository.GetRepository<Founder>().UpdateAsync(founder);
                await _repository.SaveChangesAsync();

                return _mapper.Map<FounderDto>(founder);
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error updating founder.";
                _logger.LogError(ex, "Error in UpdateFounderAsync");
                return null;
            }
        }

        public async Task<bool> DeleteFounderAsync(string encryptedFounderId, CancellationToken ct = default)
        {
            try
            {
                var founder = await FindFounderAsync(encryptedFounderId, ct);
                if (founder == null)
                    return false;

                // Soft delete only
                founder.AuditProperties.IsDeleted = true;
                founder.AuditProperties.IsActive = false;
                founder.AuditProperties.UpdatedDate = DateTime.UtcNow;
                founder.AuditProperties.UpdatedBy = _userContext.UserId;

                await _repository.GetRepository<Founder>().UpdateAsync(founder);
                await _repository.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = "Error deleting founder.";
                _logger.LogError(ex, "Error in DeleteFounderAsync");
                return false;
            }
        }

        #endregion

        #region Helpers

        private IQueryable<Founder> ActiveFoundersQuery() =>
            _repository.GetRepository<Founder>()
                .GetQuery(f => f.AuditProperties.IsActive && !f.AuditProperties.IsDeleted)
                .AsNoTracking()
                .OrderBy(f => f.DisplayOrder)
                .ThenBy(f => f.Id);

        private async Task<Dictionary<string, string?>> LoadContentMapAsync(CancellationToken ct)
        {
            var rows = await _repository.GetRepository<SiteContent>()
                .GetQuery(c => c.AuditProperties.IsActive && !c.AuditProperties.IsDeleted)
                .AsNoTracking()
                .Select(c => new { c.ContentKey, c.ContentValue })
                .ToListAsync(ct);

            return rows.ToDictionary(r => r.ContentKey, r => r.ContentValue);
        }

        /// <summary>
        /// Insert-or-update one SiteContents row. TRACKED read (no AsNoTracking) —
        /// the entity is mutated and saved by the caller's SaveChangesAsync.
        /// </summary>
        private async Task UpsertContentAsync(string key, string? value, CancellationToken ct)
        {
            var repo = _repository.GetRepository<SiteContent>();
            var row = await repo.GetQuery(c => c.ContentKey == key && !c.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (row == null)
            {
                await repo.AddAsync(new SiteContent
                {
                    ContentKey = key,
                    ContentValue = value,
                    AuditProperties = new Models.Data.Common.AuditProperties
                    {
                        IsActive = true,
                        IsDeleted = false,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = _userContext.UserId
                    }
                });
            }
            else
            {
                row.ContentValue = value;
                row.AuditProperties.UpdatedDate = DateTime.UtcNow;
                row.AuditProperties.UpdatedBy = _userContext.UserId;
                await repo.UpdateAsync(row);
            }
        }

        private async Task<Founder?> FindFounderAsync(string encryptedFounderId, CancellationToken ct)
        {
            int founderId;
            try
            {
                founderId = Convert.ToInt32(_encryptionService.Decrypt(encryptedFounderId));
            }
            catch
            {
                this.ErrorMessage = "Invalid founder id.";
                return null;
            }

            var founder = await _repository.GetRepository<Founder>()
                .GetQuery(f => f.Id == founderId && !f.AuditProperties.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (founder == null)
                this.ErrorMessage = "Founder not found.";

            return founder;
        }

        private bool ValidateFounder(SaveFounderRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                this.ErrorMessage = "Founder name is required.";
                return false;
            }

            if (request.PhotoBase64 != null && request.PhotoBase64.Length > MaxImageBase64Length)
            {
                this.ErrorMessage = "Photo is too large. Please upload an image under 1MB.";
                return false;
            }

            return true;
        }

        #endregion
    }
}
