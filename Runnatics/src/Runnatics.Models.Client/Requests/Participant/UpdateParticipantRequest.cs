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

        /// <summary>
        /// Run status: OK, DNF, Disqualified, DNS
        /// </summary>
        public string? RunStatus { get; set; }

        /// <summary>
        /// Required when RunStatus is "Disqualified"
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

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (RunStatus != null)
            {
                var validStatuses = new[] { "OK", "DNF", "Disqualified", "DNS" };
                if (!validStatuses.Contains(RunStatus, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new ValidationResult(
                        "RunStatus must be one of: OK, DNF, Disqualified, DNS",
                        new[] { nameof(RunStatus) });
                }

                if (string.Equals(RunStatus, "Disqualified", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(DisqualificationReason))
                {
                    yield return new ValidationResult(
                        "DisqualificationReason is required when RunStatus is Disqualified",
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
}
