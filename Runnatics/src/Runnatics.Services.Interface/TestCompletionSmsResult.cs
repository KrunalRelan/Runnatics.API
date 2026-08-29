namespace Runnatics.Services.Interface
{
    /// <summary>
    /// Outcome of a single-participant test completion SMS, including the rendered template
    /// variables so the caller can show exactly what was sent.
    /// </summary>
    public class TestCompletionSmsResult
    {
        public bool Success { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>Masked destination actually used.</summary>
        public string Recipient { get; set; } = string.Empty;

        public bool UsedOverridePhone { get; set; }

        public string NameWithBib { get; set; } = string.Empty;
        public string FinishTime { get; set; } = string.Empty;
        public string RaceTitle { get; set; } = string.Empty;

        public static TestCompletionSmsResult Fail(string error) =>
            new() { Success = false, ErrorMessage = error };
    }
}
