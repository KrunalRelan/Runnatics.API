namespace Runnatics.Models.Client.Public
{
    public class PublicGalleryImageDto
    {
        public int Id { get; set; }

        public string Url { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; }

        public string? Caption { get; set; }

        public string? EventName { get; set; }

        public DateTime? EventDate { get; set; }
    }
}
