namespace Runnatics.Models.Client.Public
{
    public class PublicGenderGroupDto
    {
        public string Gender { get; set; } = string.Empty;
        public List<PublicCategoryGroupDto> Categories { get; set; } = [];
    }
}
