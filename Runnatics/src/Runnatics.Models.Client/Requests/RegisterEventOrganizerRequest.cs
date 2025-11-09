using System.ComponentModel.DataAnnotations;

namespace Runnatics.API.Models.Requests;

public class EventOrganizerRequest
{
    [Required]
    public Guid EventId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string EventOrganizerName { get; set; } = string.Empty;
}