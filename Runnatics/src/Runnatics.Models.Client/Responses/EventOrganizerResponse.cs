namespace Runnatics.Models.Client.Responses;

public class EventOrganizerResponse
{
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; }
}
