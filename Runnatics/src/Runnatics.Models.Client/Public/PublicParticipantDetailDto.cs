namespace Runnatics.Models.Client.Public
{
    public class PublicParticipantDetailDto
    {
        public string EventName { get; set; } = string.Empty;
        public string? RaceDate { get; set; }
        public string? EventBannerBase64 { get; set; }
        public bool CertificateAvailable { get; set; }
        public string? CertificateUrl { get; set; }
        public PublicParticipantInfoDto Participant { get; set; } = new();
        public PublicTimeDetailDto? ChipTime { get; set; }
        public PublicTimeDetailDto? GunTime { get; set; }
        public List<PublicSplitDetailDto> Splits { get; set; } = [];
    }
}
