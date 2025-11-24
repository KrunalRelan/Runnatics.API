namespace Runnatics.Models.Client.Responses.Races
{
    public class Participant
    {
        public string? Id { get; set; }

        public string? RaceId { get; set; }

        public string? EventId { get; set; }

        public string? Bib { get; set; }

        public string? Name { get; set; }

        public string? Gender { get; set; }

        public string? Category { get; set; }

        public string? Status { get; set; }

        public bool IsCheckedIn { get; set; }

        public string? ChipId { get; set; }
    }
}