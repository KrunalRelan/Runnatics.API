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
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        ISmsService smsService,
        ILogger<SupportQueryService> logger) : ServiceBase<IUnitOfWork<RaceSyncDbContext>>(repository), ISupportQueryService
    {
        private readonly IEmailService _emailService = emailService;
        private readonly IEmailTemplateService _emailTemplateService = emailTemplateService;
        private readonly ISmsService _smsService = smsService;
        private readonly ILogger<SupportQueryService> _logger = logger;

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

                await SendSubmissionConfirmationAsync(query.SubmitterEmail, query.Subject, query.Id);

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

                await SendSubmissionConfirmationAsync(query.SubmitterEmail, query.Subject, query.Id);

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

                var counts = await repo.GetQuery()
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

                var query = repo.GetQuery()
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

                var query = await repo.GetQuery(q => q.Id == id)
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

                var query = await repo.GetQuery(q => q.Id == id).FirstOrDefaultAsync();

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

                var query = await queryRepo.GetQuery(q => q.Id == id).FirstOrDefaultAsync();

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
                    await SendCommentEmailInternalAsync(comment, query.SubmitterEmail);
                    comment.NotificationSent = true;
                    await commentRepo.UpdateAsync(comment);
                    await _repository.SaveChangesAsync();
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

                var comment = await commentRepo.GetQuery(c => c.Id == commentId)
                    .Include(c => c.SupportQuery)
                    .FirstOrDefaultAsync();

                if (comment == null)
                {
                    ErrorMessage = "Comment not found.";
                    return;
                }

                await SendCommentEmailInternalAsync(comment, comment.SupportQuery.SubmitterEmail);

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

                var comment = await commentRepo.GetQuery(c => c.Id == commentId).FirstOrDefaultAsync();

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

        private async Task SendCommentEmailInternalAsync(SupportQueryComment comment, string recipientEmail)
        {
            try
            {
                var subject = "Update on your support query";
                var htmlBody = _emailTemplateService.BuildSupportQueryReply(
                    submitterName: recipientEmail,
                    subject: subject,
                    replyBody: comment.CommentText);
                await _emailService.SendEmailAsync(recipientEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send comment notification email to {Email}", recipientEmail);
            }
        }

        private async Task SendSubmissionConfirmationAsync(string submitterEmail, string subject, int ticketId)
        {
            try
            {
                var htmlBody = _emailTemplateService.BuildSupportQueryConfirmation(
                    submitterName: submitterEmail,
                    subject: subject,
                    ticketId: ticketId.ToString());
                await _emailService.SendEmailAsync(submitterEmail, "We received your support query", htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send submission confirmation email to {Email}", submitterEmail);
            }
        }

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
