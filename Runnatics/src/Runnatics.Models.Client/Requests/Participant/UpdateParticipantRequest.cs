using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Participant
{
    public class UpdateParticipantRequest : IValidatableObject
    {
        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        [RegularExpression(@"^\+?[\d\s\-\(\)]{10,15}$",
            ErrorMessage = "Mobile number must be 10-15 digits and can contain +, -, (), and spaces")]
        public string? Mobile { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address format")]
        public string? Email { get; set; }

        public string? AgeCategory { get; set; }

        public DateTime? DateOfBirth { get; set; }

        /// <summary>
        /// #4/#5 (2026-07-03): run status is COMPUTED from timing data (OK/DNF/DNS — #7 rules);
        /// the ONLY manually settable value is DSQ (accepted spellings: "DSQ" / "DQ" /
        /// "Disqualified" — normalized to the stored "DQ"). Any other value → 400.
        /// </summary>
        public string? RunStatus { get; set; }

        /// <summary>
        /// MANDATORY when RunStatus is DSQ.
        /// </summary>
        public string? DisqualificationReason { get; set; }

        public decimal? ManualDistance { get; set; }

        public int? LoopCount { get; set; }

        /// <summary>
        /// Admin-entered finish time in HH:MM:SS format
        /// </summary>
        public string? ManualTime { get; set; }

        /// <summary>
        /// Encrypted race ID. When provided and different from current race, participant is reassigned.
        /// </summary>
        public string? RaceId { get; set; }

        /// <summary>
        /// Manual checkpoint crossing times. Saving these marks the participant as IsManualTiming = true.
        /// </summary>
        public List<ManualCheckpointTime>? ManualCheckpointTimes { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (RunStatus != null)
            {
                // #4 (2026-07-03): OK/DNF/DNS are COMPUTED-ONLY (#7 gate-coverage rules) — the
                // sole manual override is DSQ.
                var dsqSpellings = new[] { "DSQ", "DQ", "Disqualified" };
                if (!dsqSpellings.Contains(RunStatus, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new ValidationResult(
                        "Run status is computed from timing data — only DSQ can be set manually.",
                        new[] { nameof(RunStatus) });
                }
                else if (string.IsNullOrWhiteSpace(DisqualificationReason))
                {
                    // #5: the reason is MANDATORY for a disqualification.
                    yield return new ValidationResult(
                        "DisqualificationReason is required when setting DSQ.",
                        new[] { nameof(DisqualificationReason) });
                }
            }

            if (ManualTime != null && !TimeSpan.TryParseExact(ManualTime, @"hh\:mm\:ss", null, out _))
            {
                yield return new ValidationResult(
                    "ManualTime must be in HH:MM:SS format",
                    new[] { nameof(ManualTime) });
            }
        }
    }

    public class ManualCheckpointTime
    {
        public string CheckpointId { get; set; } = string.Empty; // Encrypted checkpoint ID

        /// <summary>
        /// Absolute crossing time, e.g. "2024-04-01T10:30:00"
        /// </summary>
        public DateTime Time { get; set; }
    }
}
