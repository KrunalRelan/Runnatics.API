namespace Runnatics.Models.Client.Public
{
    public class PublicRaceCategoryDto
    {
        public string EncryptedRaceId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        // Maps from Race.Distance
        public string? Distance { get; set; }

        // Race entity does not have a Price column — set to null until column is added
        public decimal? Price { get; set; }

        // Maps from Race.MaxParticipants
        public int? ParticipantLimit { get; set; }

        // Computed: count of active, non-deleted Participants for this race
        public int? RegisteredCount { get; set; }

        public bool HasResults { get; set; }
    }
}
