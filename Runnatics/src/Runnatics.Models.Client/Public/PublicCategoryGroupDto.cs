namespace Runnatics.Models.Client.Public
{
    public class PublicCategoryGroupDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public string RankBy { get; set; } = "Chip time";
        public List<PublicLeaderboardEntryDto> Participants { get; set; } = [];
    }
}
