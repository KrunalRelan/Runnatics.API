using Runnatics.Models.Client.Requests.Support;
using Runnatics.Models.Client.Responses.Support;

namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Service for managing support queries submitted via the Contact Us page
    /// </summary>
    public interface ISupportQueryService
    {
        /// <summary>
        /// Gets the error message from the last operation
        /// </summary>
        string ErrorMessage { get; }

        /// <summary>
        /// Indicates whether the last operation produced an error
        /// </summary>
        bool HasError { get; }

        /// <summary>
        /// Submits a new support query from the public Contact Us page (no auth required)
        /// </summary>
        Task<int> SubmitQueryAsync(ContactUsRequestDto dto);

        /// <summary>
        /// Creates a support query from the public marketing site contact form.
        /// Accepts the richer set of fields (name, phone, eventName) that the
        /// public form collects. Extra fields are embedded in the body since the
        /// SupportQuery schema only stores Subject/Body/SubmitterEmail.
        /// Returns the new query ID, or 0 on failure (check HasError / ErrorMessage).
        /// </summary>
        Task<int> CreatePublicQueryAsync(
            string name,
            string email,
            string? phone,
            string subject,
            string message,
            string? eventName = null);

        /// <summary>
        /// Returns counts grouped by status for the admin dashboard
        /// </summary>
        Task<SupportQueryCountsDto> GetCountsAsync();

        /// <summary>
        /// Returns a paged, filtered list of support queries
        /// </summary>
        Task<(List<SupportQueryListItemDto> Items, int TotalCount)> GetQueriesAsync(
            string? submitterEmail,
            int? statusId,
            int? queryTypeId,
            int? assignedToUserId,
            int page,
            int pageSize);

        /// <summary>
        /// Returns full detail for a single support query including its comments
        /// </summary>
        Task<SupportQueryDetailDto?> GetQueryByIdAsync(int id);

        /// <summary>
        /// Updates the status, assignee, or type of a support query
        /// </summary>
        Task UpdateQueryAsync(int id, UpdateQueryRequestDto dto);

        /// <summary>
        /// Adds a comment to a support query and optionally sends a notification email
        /// </summary>
        Task<SupportQueryCommentDto> AddCommentAsync(int id, AddCommentRequestDto dto, int adminUserId);

        /// <summary>
        /// Sends (or re-sends) the notification email for a specific comment
        /// </summary>
        Task SendCommentEmailAsync(int commentId);

        /// <summary>
        /// Hard-deletes a comment
        /// </summary>
        Task DeleteCommentAsync(int commentId);
    }
}
