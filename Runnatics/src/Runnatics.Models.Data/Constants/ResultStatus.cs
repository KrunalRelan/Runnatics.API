namespace Runnatics.Models.Data.Constants
{
    public static class ResultStatus
    {
        public const string Finished = "Finished";
        public const string DNF = "DNF";
        public const string DNS = "DNS";
        public const string DQ = "DQ";

        /// <summary>
        /// Display label for a STORED status (#7/#5, client-confirmed 2026-07-03): "Finished"
        /// renders as "OK" and "DQ" renders as "DSQ" on every surface (grid, leaderboard,
        /// details, public site, export). DISPLAY MAPPING ONLY — stored values stay
        /// "Finished"/"DQ" (migration is a later pass). Unknown / null stored values pass
        /// through unchanged.
        /// </summary>
        public static string ToDisplay(string? stored) => stored switch
        {
            Finished => "OK",
            DQ => "DSQ",
            _ => stored ?? string.Empty
        };

        /// <summary>
        /// #5: normalize any DSQ spelling from the API boundary ("DSQ" / "Disqualified" / "DQ",
        /// any case) to the ONE canonical stored value "DQ" — the enum-vs-string trap has bitten
        /// this codebase before; storage must never hold mixed spellings.
        /// </summary>
        public static bool IsDsq(string? value) =>
            string.Equals(value, DQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "DSQ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Disqualified", StringComparison.OrdinalIgnoreCase);
    }
}
