namespace Runnatics.Models.Client.Responses.Participants
{
    /// <summary>
    /// Ranking information for a participant across different categories
    /// </summary>
    public class RankingInfo
    {
        /// <summary>
        /// Overall rank among all participants
        /// </summary>
        public int? OverallRank { get; set; }

        /// <summary>
        /// Total number of participants in the race
        /// </summary>
        public int? TotalParticipants { get; set; }

        /// <summary>
        /// Percentile ranking among all participants
        /// </summary>
        public decimal? OverallPercentage { get; set; }

        /// <summary>
        /// Rank within gender category
        /// </summary>
        public int? GenderRank { get; set; }

        /// <summary>
        /// Total participants in the same gender category
        /// </summary>
        public int? TotalInGender { get; set; }

        /// <summary>
        /// Percentile ranking within gender category
        /// </summary>
        public decimal? GenderPercentage { get; set; }

        /// <summary>
        /// Rank within age category
        /// </summary>
        public int? CategoryRank { get; set; }

        /// <summary>
        /// Total participants in the same age category
        /// </summary>
        public int? TotalInCategory { get; set; }

        /// <summary>
        /// Percentile ranking within age category
        /// </summary>
        public decimal? CategoryPercentage { get; set; }

        /// <summary>
        /// Rank across all categories (optional additional ranking)
        /// </summary>
        public int? AllCategoriesRank { get; set; }

        /// <summary>
        /// Total participants across all categories
        /// </summary>
        public int? TotalAllCategories { get; set; }

        /// <summary>
        /// Percentile ranking across all categories
        /// </summary>
        public decimal? AllCategoriesPercentage { get; set; }
    }
}
