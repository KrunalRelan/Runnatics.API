using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Support
{
    public class AddCommentRequestDto
    {
        [Required]
        public string CommentText { get; set; } = string.Empty;

        [Required]
        public int TicketStatusId { get; set; }

        public bool SendNotification { get; set; } = false;
    }
}
