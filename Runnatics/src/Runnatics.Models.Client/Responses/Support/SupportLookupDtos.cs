namespace Runnatics.Models.Client.Responses.Support
{
    /// <summary>
    /// A support lookup row (status or query type). The UI used to hard-code these;
    /// they now come from the DB, which is the only source that can't drift.
    /// </summary>
    public class SupportLookupDto
    {
        public int Id { get; set; }

        /// <summary>Raw stored value, e.g. "new_query". Match on this, never display it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable label, e.g. "New Query". Derived server-side so the raw ->
        /// display mapping lives in exactly ONE place — the enum-vs-DB-string split has
        /// bitten this codebase before.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>A user who can be assigned a support ticket.</summary>
    public class SupportAssigneeDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
