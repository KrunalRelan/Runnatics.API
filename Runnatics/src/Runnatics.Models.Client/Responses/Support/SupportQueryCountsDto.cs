namespace Runnatics.Models.Client.Responses.Support
{
    /// <summary>
    /// Ticket counts per status, keyed by STATUS ID — never by label string.
    /// The previous shape had one fixed property per seeded status name
    /// ("NewQuery", "Wip", …) and matched them against hardcoded English labels, so a
    /// database whose lookup table held different names (e.g. "Open"/"Resolved")
    /// reported 0 in every bucket while Total still showed the real figure.
    /// </summary>
    public class SupportQueryCountsDto
    {
        /// <summary>Total tickets in scope. ALWAYS equals the sum of <see cref="Statuses"/>.</summary>
        public int Total { get; set; }

        /// <summary>
        /// One entry per status. Includes statuses with zero tickets (so the dashboard keeps
        /// a stable set of cards) AND any StatusId present in the data but absent from the
        /// lookup table (so no ticket is ever invisible).
        /// </summary>
        public List<SupportStatusCountDto> Statuses { get; set; } = [];
    }

    public class SupportStatusCountDto
    {
        public int StatusId { get; set; }

        /// <summary>Raw stored value, e.g. "new_query" or "Open". Match on ID, not on this.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Human label derived from <see cref="Name"/>.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Badge colour, resolved server-side so every surface agrees.</summary>
        public string ColorHex { get; set; } = string.Empty;

        public int Count { get; set; }

        /// <summary>
        /// True when tickets reference this StatusId but the lookup table has no such row
        /// (orphaned FK / manual data edit). Surfaced rather than hidden.
        /// </summary>
        public bool IsUnknown { get; set; }
    }
}
