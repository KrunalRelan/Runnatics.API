namespace Runnatics.Models.Client.Responses.Certificates
{
    public class CertificateTemplateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string? RaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? BackgroundImageUrl { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<CertificateFieldResponse> Fields { get; set; } = new();
    }
}
