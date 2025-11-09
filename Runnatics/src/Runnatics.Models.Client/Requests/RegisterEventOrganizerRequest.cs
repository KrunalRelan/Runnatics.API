using System.ComponentModel.DataAnnotations;

namespace Runnatics.API.Models.Requests;

public class EventOrganizerRequest
{
    [Required]
    public int OrganizationId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string EventOrganizerName { get; set; } = string.Empty;
}