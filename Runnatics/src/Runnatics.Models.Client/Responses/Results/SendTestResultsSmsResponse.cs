namespace Runnatics.Models.Client.Responses.Results
{
    /// <summary>
    /// Result of the single-participant "Send Test SMS" action. Carries the rendered template
    /// variables and the provider's message id so the operator can confirm the message content
    /// and match it to the MSG91 dashboard row without reading a handset.
    /// </summary>
    public class SendTestResultsSmsResponse
    {
        /// <summary>Masked destination actually used (override phone if supplied, else the participant's).</summary>
        public string Recipient { get; set; } = string.Empty;

        /// <summary>True when the override phone was used rather than the participant's stored number.</summary>
        public bool UsedOverridePhone { get; set; }

        /// <summary>True when MSG91 accepted the send.</summary>
        public bool Success { get; set; }

        /// <summary>MSG91's id for the send — the key to find this message in their dashboard.</summary>
        public string? ProviderMessageId { get; set; }

        /// <summary>Provider or transport error when Success is false.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>var1 — participant name with bib, e.g. "Deepender[1244]".</summary>
        public string NameWithBib { get; set; } = string.Empty;

        /// <summary>var2 — finish time as sent.</summary>
        public string FinishTime { get; set; } = string.Empty;

        /// <summary>var3 — race title as sent.</summary>
        public string RaceTitle { get; set; } = string.Empty;
    }
}
