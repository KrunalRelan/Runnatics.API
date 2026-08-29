using System.ComponentModel.DataAnnotations;

namespace Runnatics.Models.Client.Requests.Results
{
    /// <summary>
    /// Body for the single-participant "Send Test SMS" action.
    /// </summary>
    public class SendTestResultsSmsRequest
    {
        /// <summary>
        /// Send to this number instead of the participant's stored phone. Lets an operator send
        /// the real message shape to their own handset without messaging a runner. Leave null or
        /// blank to use the participant's own number.
        /// </summary>
        [MaxLength(20)]
        [RegularExpression(@"^\+?[0-9][0-9 \-]{6,19}$", ErrorMessage = "Override phone must be 7-20 digits, optionally starting with '+'.")]
        public string? OverridePhone { get; set; }
    }
}
