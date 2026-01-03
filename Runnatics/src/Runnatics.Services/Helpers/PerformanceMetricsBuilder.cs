using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for calculating performance metrics from split times
    /// Follows Single Responsibility Principle - only handles performance calculations
    /// </summary>
    public class PerformanceMetricsBuilder
    {
        private readonly IEncryptionService _encryptionService;

        public PerformanceMetricsBuilder(IEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public void BuildSplitTimesAndPerformance(ParticipantDetailsResponse response, List<SplitTime> splitTimes)
        {
            response.SplitTimes = new List<SplitTimeInfo>();
            response.PaceProgression = new List<PaceProgressionInfo>();

            var metrics = new PerformanceMetrics();
            long previousCumulativeMs = 0;

            foreach (var st in splitTimes)
            {
                var checkpoint = st.Checkpoint;
                var distanceKm = checkpoint?.DistanceFromStart ?? 0;
                var segmentDistanceKm = distanceKm - metrics.TotalDistance;
                metrics.TotalDistance = distanceKm;

                var segmentTimeMs = st.SegmentTime ?? (st.SplitTimeMs - previousCumulativeMs);
                previousCumulativeMs = st.SplitTimeMs;

                var (paceValue, speedValue) = CalculatePaceAndSpeed(segmentDistanceKm, segmentTimeMs);

                if (paceValue.HasValue && speedValue.HasValue)
                {
                    metrics.UpdateMetrics(paceValue.Value, speedValue.Value);
                }

                var splitTimeInfo = BuildSplitTimeInfo(st, checkpoint, distanceKm, segmentTimeMs, paceValue, speedValue);
                response.SplitTimes.Add(splitTimeInfo);

                var progressionInfo = BuildPaceProgressionInfo(checkpoint?.Name, distanceKm, splitTimeInfo);
                response.PaceProgression.Add(progressionInfo);
            }

            response.Performance = metrics.BuildPerformanceOverview();
        }

        private SplitTimeInfo BuildSplitTimeInfo(
            SplitTime st,
            Checkpoint? checkpoint,
            decimal distanceKm,
            long segmentTimeMs,
            decimal? paceValue,
            decimal? speedValue)
        {
            return new SplitTimeInfo
            {
                CheckpointId = checkpoint != null ? _encryptionService.Encrypt(checkpoint.Id.ToString()) : null,
                CheckpointName = checkpoint?.Name,
                Distance = $"{distanceKm} km",
                DistanceKm = distanceKm,
                SplitTime = TimeFormatter.FormatTimeSpan(segmentTimeMs),
                CumulativeTime = TimeFormatter.FormatTimeSpan(st.SplitTimeMs),
                Pace = paceValue.HasValue ? TimeFormatter.FormatPace(paceValue.Value) : null,
                PaceValue = paceValue.HasValue ? Math.Round(paceValue.Value, 2) : null,
                Speed = speedValue.HasValue ? Math.Round(speedValue.Value, 1) : null,
                OverallRank = st.Rank,
                GenderRank = st.GenderRank,
                CategoryRank = st.CategoryRank
            };
        }

        private static PaceProgressionInfo BuildPaceProgressionInfo(
            string? checkpointName,
            decimal distanceKm,
            SplitTimeInfo splitTimeInfo)
        {
            var progressionSegment = GetPaceProgressionSegment(checkpointName, distanceKm);
            return new PaceProgressionInfo
            {
                Segment = progressionSegment,
                Pace = splitTimeInfo.Pace,
                PaceValue = splitTimeInfo.PaceValue,
                Speed = splitTimeInfo.Speed,
                SplitTime = splitTimeInfo.SplitTime
            };
        }

        private static (decimal? paceValue, decimal? speedValue) CalculatePaceAndSpeed(decimal segmentDistanceKm, long segmentTimeMs)
        {
            if (segmentDistanceKm <= 0 || segmentTimeMs <= 0)
            {
                return (null, null);
            }

            // Pace in minutes per km
            var paceValue = (decimal)segmentTimeMs / 60000m / segmentDistanceKm;
            // Speed in km/h
            var speedValue = segmentDistanceKm / ((decimal)segmentTimeMs / 3600000m);

            return (paceValue, speedValue);
        }

        private static string GetPaceProgressionSegment(string? checkpointName, decimal distanceKm)
        {
            if (!string.IsNullOrEmpty(checkpointName))
            {
                if (checkpointName.Contains("finish", StringComparison.OrdinalIgnoreCase) ||
                    checkpointName.Contains("end", StringComparison.OrdinalIgnoreCase))
                    return "FINISH";

                if (checkpointName.EndsWith("K", StringComparison.OrdinalIgnoreCase) ||
                    checkpointName.EndsWith("km", StringComparison.OrdinalIgnoreCase))
                    return checkpointName.ToUpper().Replace("KM", "K");
            }

            if (distanceKm >= 21) return "FINISH";
            return $"{(int)distanceKm}K";
        }

        private class PerformanceMetrics
        {
            public decimal TotalDistance { get; set; }
            public decimal? BestPaceValue { get; private set; }
            public decimal? MaxSpeed { get; private set; }
            public decimal TotalPace { get; private set; }
            public decimal TotalSpeed { get; private set; }
            public int PaceCount { get; private set; }

            public void UpdateMetrics(decimal paceValue, decimal speedValue)
            {
                TotalPace += paceValue;
                TotalSpeed += speedValue;
                PaceCount++;

                if (!BestPaceValue.HasValue || paceValue < BestPaceValue)
                    BestPaceValue = paceValue;

                if (!MaxSpeed.HasValue || speedValue > MaxSpeed)
                    MaxSpeed = speedValue;
            }

            public PerformanceOverview BuildPerformanceOverview()
            {
                return new PerformanceOverview
                {
                    AverageSpeed = PaceCount > 0 ? Math.Round(TotalSpeed / PaceCount, 2) : null,
                    AveragePace = PaceCount > 0 ? TimeFormatter.FormatPace(TotalPace / PaceCount) : null,
                    MaxSpeed = MaxSpeed.HasValue ? Math.Round(MaxSpeed.Value, 1) : null,
                    BestPace = BestPaceValue.HasValue ? TimeFormatter.FormatPace(BestPaceValue.Value) : null
                };
            }
        }
    }
}
