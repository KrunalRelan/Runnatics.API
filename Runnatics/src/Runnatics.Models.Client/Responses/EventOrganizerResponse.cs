namespace Runnatics.Models.Client.Responses;

public class EventOrganizerResponse
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
}
