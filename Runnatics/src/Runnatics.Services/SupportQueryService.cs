using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runnatics.Data.EF;
using Runnatics.Models.Client.Requests.Support;
using Runnatics.Models.Client.Responses.Support;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;
using Runnatics.Services.Interface;

namespace Runnatics.Services
{
    public class SupportQueryService(
        IUnitOfWork<RaceSyncDbContext> repository,
        ISmsService smsService,
        IRaceNotificationService raceNotificationService,
        IUserContextService userContext,
        ILogger<SupportQueryService> logger) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), ISupportQueryService
    {
        // IEmailService / IEmailTemplateService are no longer injected: this service does
        // not send email directly any more — everything goes via IRaceNotificationService
        // so it lands in NotificationLogs.
        private readonly ISmsService _smsService = smsService;
        private readonly IRaceNotificationService _raceNotificationService = raceNotificationService;
        private readonly IUserContextService _userContext = userContext;
        private readonly ILogger<SupportQueryService> _logger = logger;

        private const string SuperAdminRole = "SuperAdmin";

        /// <summary>
        /// Tenant restriction for the CURRENT caller. NULL means "no restriction" —
        /// SuperAdmin sees every tenant plus the platform pool (TenantId IS NULL).
        /// Any other role is pinned to its own tenant and can never see the platform pool.
        /// Admin-only: never call this from the [AllowAnonymous] submit paths.
        /// </summary>
        private int? CurrentTenantScope() =>
            string.Equals(_userContext.Role, SuperAdminRole, StringComparison.OrdinalIgnoreCase)
                ? null
                : _userContext.TenantId;

        /// <summary>
        /// Applies <see cref="CurrentTenantScope"/> to a SupportQuery query. Out-of-tenant
        /// rows are filtered out rather than rejected, so callers surface them as
        /// "not found" and never leak the existence of another tenant's ticket.
        /// </summary>
        private static IQueryable<SupportQuery> ApplyTenantScope(IQueryable<SupportQuery> query, int? tenantScope) =>
            tenantScope is null ? query : query.Where(q => q.TenantId == tenantScope.Value);

        /// <summary>
        /// Comment-side equivalent of <see cref="ApplyTenantScope"/>: scopes through the
        /// PARENT ticket, so a raw comment id can never be used to reach across tenants.
        /// Also eager-loads SupportQuery, which the email path needs.
        /// </summary>
        private IQueryable<SupportQueryComment> ScopedComments(IQueryable<SupportQueryComment> query)
        {
            var scoped = query.Include(c => c.SupportQuery).AsQueryable();
            var tenantScope = CurrentTenantScope();
            return tenantScope is null
                ? scoped
                : scoped.Where(c => c.SupportQuery.TenantId == tenantScope.Value);
        }

        // ── Public (no auth) ──────────────────────────────────────────────────

        public async Task<int> SubmitQueryAsync(ContactUsRequestDto dto)
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                var query = new SupportQuery
                {
                    Subject = dto.Subject,
                    Body = dto.Body,
                    SubmitterEmail = dto.SubmitterEmail.Trim().ToLowerInvariant(),
                    StatusId = 1, // new_query
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await repo.AddAsync(query);
                await _repository.SaveChangesAsync();

                _logger.LogInformation("Support query submitted by {Email}, Id: {Id}", query.SubmitterEmail, query.Id);

                await _raceNotificationService.NotifySupportTicketCreatedAsync(query.Id);

                return query.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting support query for {Email}", dto.SubmitterEmail);
                ErrorMessage = "An error occurred while submitting your query.";
                return 0;
            }
        }

        public async Task<int> CreatePublicQueryAsync(
            string name,
            string email,
            string? phone,
            string subject,
            string message,
            string? eventName = null)
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                // SupportQuery has no separate Name/Phone/EventName columns.
                // Embed them in the body so admins see the full context.
                var bodyBuilder = new System.Text.StringBuilder();
                bodyBuilder.AppendLine($"Name: {name}");
                if (!string.IsNullOrWhiteSpace(phone))
                    bodyBuilder.AppendLine($"Phone: {phone}");
                if (!string.IsNullOrWhiteSpace(eventName))
                    bodyBuilder.AppendLine($"Event: {eventName}");
                bodyBuilder.AppendLine();
                bodyBuilder.Append(message);

                var query = new SupportQuery
                {
                    Subject  = subject,
                    Body     = bodyBuilder.ToString(),
                    SubmitterEmail = email.Trim().ToLowerInvariant(),
                    StatusId = 1, // new_query
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await repo.AddAsync(query);
                await _repository.SaveChangesAsync();

                _logger.LogInformation(
                    "Public contact form submitted by {Email} (name: {Name}), Id: {Id}",
                    query.SubmitterEmail, name, query.Id);

                await _raceNotificationService.NotifySupportTicketCreatedAsync(query.Id);

                return query.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating public query for {Email}", email);
                ErrorMessage = "An error occurred while submitting your message.";
                return 0;
            }
        }

        // ── Admin ─────────────────────────────────────────────────────────────

        public async Task<SupportQueryCountsDto> GetCountsAsync()
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                var counts = await ApplyTenantScope(repo.GetQuery(), CurrentTenantScope())
                    .GroupBy(q => q.Status.Name)
                    .Select(g => new { StatusName = g.Key, Count = g.Count() })
                    .ToListAsync();

                var total = counts.Sum(c => c.Count);

                return new SupportQueryCountsDto
                {
                    Total        = total,
                    NewQuery     = counts.FirstOrDefault(c => c.StatusName == "new_query")?.Count     ?? 0,
                    Wip          = counts.FirstOrDefault(c => c.StatusName == "wip")?.Count          ?? 0,
                    Closed       = counts.FirstOrDefault(c => c.StatusName == "closed")?.Count       ?? 0,
                    Pending      = counts.FirstOrDefault(c => c.StatusName == "pending")?.Count      ?? 0,
                    NotYetStarted = counts.FirstOrDefault(c => c.StatusName == "not_yet_started")?.Count ?? 0,
                    Rejected     = counts.FirstOrDefault(c => c.StatusName == "rejected")?.Count     ?? 0,
                    Duplicate    = counts.FirstOrDefault(c => c.StatusName == "duplicate")?.Count    ?? 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support query counts");
                ErrorMessage = "An error occurred while retrieving counts.";
                return new SupportQueryCountsDto();
            }
        }

        public async Task<List<SupportLookupDto>> GetStatusesAsync()
        {
            try
            {
                var statuses = await _repository.GetRepository<SupportQueryStatus>()
                    .GetQuery()
                    .AsNoTracking()
                    .OrderBy(s => s.Id)
                    .Select(s => new SupportLookupDto { Id = s.Id, Name = s.Name })
                    .ToListAsync();

                foreach (var dto in statuses) dto.DisplayName = ToDisplayName(dto.Name);
                return statuses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support statuses");
                ErrorMessage = "An error occurred while retrieving statuses.";
                return [];
            }
        }

        public async Task<List<SupportLookupDto>> GetQueryTypesAsync()
        {
            try
            {
                var types = await _repository.GetRepository<SupportQueryType>()
                    .GetQuery()
                    .AsNoTracking()
                    .OrderBy(t => t.Name)
                    .Select(t => new SupportLookupDto { Id = t.Id, Name = t.Name })
                    .ToListAsync();

                // Query types are admin-authored free text, so the raw name IS the label.
                foreach (var dto in types) dto.DisplayName = dto.Name;
                return types;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support query types");
                ErrorMessage = "An error occurred while retrieving query types.";
                return [];
            }
        }

        public async Task<List<SupportAssigneeDto>> GetAssigneesAsync()
        {
            try
            {
                // Only roles that can actually OPEN a ticket are assignable — assigning to
                // someone the endpoint authorization would reject is a dead end.
                var assignableRoles = new[] { "SuperAdmin", "Admin" };

                var query = _repository.GetRepository<User>()
                    .GetQuery(u => u.AuditProperties.IsActive && !u.AuditProperties.IsDeleted)
                    .AsNoTracking()
                    .Where(u => assignableRoles.Contains(u.Role));

                var tenantScope = CurrentTenantScope();
                if (tenantScope is not null)
                    query = query.Where(u => u.TenantId == tenantScope.Value);

                var users = await query
                    .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                    .Select(u => new SupportAssigneeDto
                    {
                        Id = u.Id,
                        Email = u.Email,
                        FullName = ((u.FirstName ?? string.Empty) + " " + (u.LastName ?? string.Empty)).Trim()
                    })
                    .ToListAsync();

                // Fall back to the email when a user has no name recorded, so the dropdown
                // never renders a blank row.
                foreach (var u in users)
                    if (string.IsNullOrWhiteSpace(u.FullName)) u.FullName = u.Email;

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support assignees");
                ErrorMessage = "An error occurred while retrieving assignable users.";
                return [];
            }
        }

        public async Task<(List<SupportQueryListItemDto> Items, int TotalCount)> GetQueriesAsync(
            string? submitterEmail,
            int? statusId,
            int? queryTypeId,
            int? assignedToUserId,
            int page,
            int pageSize)
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                var query = ApplyTenantScope(repo.GetQuery(), CurrentTenantScope())
                    .Include(q => q.Status)
                    .Include(q => q.AssignedToUser)
                    .Include(q => q.Comments)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(submitterEmail))
                    query = query.Where(q => q.SubmitterEmail.Contains(submitterEmail));

                if (statusId.HasValue)
                    query = query.Where(q => q.StatusId == statusId.Value);

                if (queryTypeId.HasValue)
                    query = query.Where(q => q.QueryTypeId == queryTypeId.Value);

                if (assignedToUserId.HasValue)
                    query = query.Where(q => q.AssignedToUserId == assignedToUserId.Value);

                var totalCount = await query.CountAsync();

                var raw = await query
                    .OrderByDescending(q => q.UpdatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var now = DateTime.UtcNow;
                var items = raw.Select(q => new SupportQueryListItemDto
                {
                    Id             = q.Id,
                    Subject        = q.Subject,
                    SubmitterEmail = q.SubmitterEmail,
                    CommentCount   = q.Comments.Count,
                    LastUpdated    = ToRelativeLabel(now - q.UpdatedAt),
                    AssignedToName = q.AssignedToUser != null
                        ? $"{q.AssignedToUser.FirstName} {q.AssignedToUser.LastName}".Trim()
                        : null,
                    StatusName     = q.Status.Name
                }).ToList();

                return (items, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support queries");
                ErrorMessage = "An error occurred while retrieving queries.";
                return (new List<SupportQueryListItemDto>(), 0);
            }
        }

        public async Task<SupportQueryDetailDto?> GetQueryByIdAsync(int id)
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                var query = await ApplyTenantScope(repo.GetQuery(q => q.Id == id), CurrentTenantScope())
                    .Include(q => q.Status)
                    .Include(q => q.QueryType)
                    .Include(q => q.AssignedToUser)
                    .Include(q => q.Comments)
                        .ThenInclude(c => c.TicketStatus)
                    .Include(q => q.Comments)
                        .ThenInclude(c => c.CreatedByUser)
                    .FirstOrDefaultAsync();

                if (query == null)
                {
                    ErrorMessage = "Support query not found.";
                    return null;
                }

                return MapToDetailDto(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching support query {Id}", id);
                ErrorMessage = "An error occurred while retrieving the query.";
                return null;
            }
        }

        public async Task UpdateQueryAsync(int id, UpdateQueryRequestDto dto)
        {
            try
            {
                var repo = _repository.GetRepository<SupportQuery>();

                var query = await ApplyTenantScope(repo.GetQuery(q => q.Id == id), CurrentTenantScope())
                    .FirstOrDefaultAsync();

                if (query == null)
                {
                    ErrorMessage = "Support query not found.";
                    return;
                }

                if (dto.StatusId.HasValue)
                    query.StatusId = dto.StatusId.Value;

                if (dto.AssignedToUserId.HasValue)
                    query.AssignedToUserId = dto.AssignedToUserId.Value == 0
                        ? null
                        : dto.AssignedToUserId.Value;

                if (dto.QueryTypeId.HasValue)
                    query.QueryTypeId = dto.QueryTypeId.Value == 0
                        ? null
                        : dto.QueryTypeId.Value;

                query.UpdatedAt = DateTime.UtcNow;

                await repo.UpdateAsync(query);
                await _repository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating support query {Id}", id);
                ErrorMessage = "An error occurred while updating the query.";
            }
        }

        public async Task<SupportQueryCommentDto> AddCommentAsync(int id, AddCommentRequestDto dto, int adminUserId)
        {
            try
            {
                var queryRepo = _repository.GetRepository<SupportQuery>();
                var commentRepo = _repository.GetRepository<SupportQueryComment>();

                var query = await ApplyTenantScope(queryRepo.GetQuery(q => q.Id == id), CurrentTenantScope())
                    .FirstOrDefaultAsync();

                if (query == null)
                {
                    ErrorMessage = "Support query not found.";
                    return new SupportQueryCommentDto();
                }

                var comment = new SupportQueryComment
                {
                    SupportQueryId   = id,
                    CommentText      = dto.CommentText,
                    TicketStatusId   = dto.TicketStatusId,
                    NotificationSent = false,
                    CreatedAt        = DateTime.UtcNow,
                    CreatedByUserId  = adminUserId
                };

                await commentRepo.AddAsync(comment);

                // Keep query UpdatedAt in sync
                query.UpdatedAt = DateTime.UtcNow;
                await queryRepo.UpdateAsync(query);

                await _repository.SaveChangesAsync();

                if (dto.SendNotification)
                {
                    // Only flag NotificationSent when the send actually succeeded — a failed
                    // send used to be recorded as sent, hiding it from the operator and
                    // disabling the "Send Email" retry button on that comment.
                    var sent = await _raceNotificationService.NotifySupportCommentAsync(comment.Id);
                    if (sent)
                    {
                        comment.NotificationSent = true;
                        await commentRepo.UpdateAsync(comment);
                        await _repository.SaveChangesAsync();
                    }
                }

                // Reload with navigation for the response DTO
                var saved = await commentRepo.GetQuery(c => c.Id == comment.Id)
                    .Include(c => c.TicketStatus)
                    .Include(c => c.CreatedByUser)
                    .FirstOrDefaultAsync();

                return MapToCommentDto(saved ?? comment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding comment to support query {Id}", id);
                ErrorMessage = "An error occurred while adding the comment.";
                return new SupportQueryCommentDto();
            }
        }

        public async Task SendCommentEmailAsync(int commentId)
        {
            try
            {
                var commentRepo = _repository.GetRepository<SupportQueryComment>();

                // Scope through the PARENT ticket — a comment id alone must not be a way
                // around tenant isolation.
                var comment = await ScopedComments(commentRepo.GetQuery(c => c.Id == commentId))
                    .FirstOrDefaultAsync();

                if (comment == null)
                {
                    ErrorMessage = "Comment not found.";
                    return;
                }

                var sent = await _raceNotificationService.NotifySupportCommentAsync(comment.Id);
                if (!sent)
                {
                    ErrorMessage = "The notification email could not be sent. Please try again.";
                    return;
                }

                comment.NotificationSent = true;
                await commentRepo.UpdateAsync(comment);
                await _repository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email for comment {CommentId}", commentId);
                ErrorMessage = "An error occurred while sending the email.";
            }
        }

        public async Task DeleteCommentAsync(int commentId)
        {
            try
            {
                var commentRepo = _repository.GetRepository<SupportQueryComment>();

                var comment = await ScopedComments(commentRepo.GetQuery(c => c.Id == commentId))
                    .FirstOrDefaultAsync();

                if (comment == null)
                {
                    ErrorMessage = "Comment not found.";
                    return;
                }

                await commentRepo.DeleteAsync(commentId);
                await _repository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting comment {CommentId}", commentId);
                ErrorMessage = "An error occurred while deleting the comment.";
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        // NOTE: comment emails now go through IRaceNotificationService.NotifySupportCommentAsync
        // so they are written to NotificationLogs like every other outbound message. The old
        // private SendCommentEmailInternalAsync (direct IEmailService, unlogged) and the dead
        // SendSubmissionConfirmationAsync (a duplicate of NotifySupportTicketCreatedAsync that
        // was never called) have both been removed.

        private static SupportQueryDetailDto MapToDetailDto(SupportQuery q) => new()
        {
            Id             = q.Id,
            Subject        = q.Subject,
            Body           = q.Body,
            SubmitterEmail = q.SubmitterEmail,
            StatusId       = q.StatusId,
            StatusName     = q.Status?.Name ?? string.Empty,
            AssignedToUserId = q.AssignedToUserId,
            AssignedToName   = q.AssignedToUser != null
                ? $"{q.AssignedToUser.FirstName} {q.AssignedToUser.LastName}".Trim()
                : null,
            QueryTypeId   = q.QueryTypeId,
            QueryTypeName = q.QueryType?.Name,
            CreatedAt     = q.CreatedAt,
            UpdatedAt     = q.UpdatedAt,
            Comments      = q.Comments
                .OrderBy(c => c.CreatedAt)
                .Select(MapToCommentDto)
                .ToList()
        };

        private static SupportQueryCommentDto MapToCommentDto(SupportQueryComment c) => new()
        {
            Id               = c.Id,
            CommentText      = c.CommentText,
            TicketStatusId   = c.TicketStatusId,
            TicketStatusName = c.TicketStatus?.Name ?? string.Empty,
            NotificationSent = c.NotificationSent,
            CreatedAt        = c.CreatedAt,
            CreatedByName    = c.CreatedByUser != null
                ? $"{c.CreatedByUser.FirstName} {c.CreatedByUser.LastName}".Trim()
                : null
        };

        /// <summary>
        /// Stored status name -> human label ("new_query" -> "New Query"). THE single
        /// raw->display mapping; the UI no longer keeps its own copy. "wip" is special-cased
        /// because generic title-casing would render it "Wip".
        /// </summary>
        private static string ToDisplayName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            if (string.Equals(raw, "wip", StringComparison.OrdinalIgnoreCase)) return "WIP";

            return string.Join(' ', raw
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        }

        private static string ToRelativeLabel(TimeSpan diff)
        {
            if (diff.TotalDays >= 1)
            {
                int days = (int)diff.TotalDays;
                return $"{days} day{(days != 1 ? "s" : "")}";
            }
            if (diff.TotalHours >= 1)
            {
                int hours = (int)diff.TotalHours;
                return $"{hours} hour{(hours != 1 ? "s" : "")}";
            }
            int minutes = Math.Max(1, (int)diff.TotalMinutes);
            return $"{minutes} minute{(minutes != 1 ? "s" : "")}";
        }
    }
}
