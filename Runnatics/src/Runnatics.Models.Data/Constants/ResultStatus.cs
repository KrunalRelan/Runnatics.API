namespace Runnatics.Models.Data.Constants
{
    public static class ResultStatus
    {
        public const string Finished = "Finished";
        public const string DNF = "DNF";
        public const string DNS = "DNS";
        public const string DQ = "DQ";

        /// <summary>
        /// Display label for a STORED status (#7, client-confirmed 2026-07-03): "Finished" renders
        /// as "OK" on every surface (grid, leaderboard, details, public site, export). DISPLAY
        /// MAPPING ONLY — the stored value stays "Finished" (migration is a later pass). Unknown /
        /// null stored values pass through unchanged.
        /// </summary>
        public static string ToDisplay(string? stored) => stored switch
        {
            Finished => "OK",
            _ => stored ?? string.Empty
        };
    }
}
