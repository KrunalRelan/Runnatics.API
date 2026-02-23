using Runnatics.Models.Client.Helpers;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Entities;
using Runnatics.Services.Interface;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for calculating performance metrics from split times
    /// </summary>
    public class PerformanceMetricsBuilder(IEncryptionService encryptionService)
    {
        private readonly IEncryptionService _encryptionService = encryptionService;

        public void BuildSplitTimesAndPerformance(ParticipantDetailsResponse response, List<SplitTimes> splitTimes)
        {
            response.SplitTimes = [];
            response.PaceProgression = [];

            if (splitTimes.Count == 0)
            {
                response.Performance = new PerformanceOverview();
                return;
            }

            var metrics = new PerformanceMetrics();
            long cumulativeTimeMs = 0;

            foreach (var st in splitTimes)
            {
                var (splitTimeInfo, progressionInfo, segmentPace, segmentSpeed) = ProcessSplitTime(st, metrics, ref cumulativeTimeMs);
                
                response.SplitTimes.Add(splitTimeInfo);
                response.PaceProgression.Add(progressionInfo);

                if (segmentPace.HasValue && segmentSpeed.HasValue)
                {
                    metrics.UpdateMetrics(segmentPace.Value, segmentSpeed.Value);
                }
            }

            response.Performance = BuildPerformanceOverview(metrics);
        }

        private (SplitTimeInfo splitInfo, PaceProgressionInfo progressionInfo, decimal? pace, decimal? speed) ProcessSplitTime(
            SplitTimes st,
            PerformanceMetrics metrics,
            ref long cumulativeTimeMs)
        {
            var checkpoint = st.ToCheckpoint;
            var distanceKm = checkpoint?.DistanceFromStart ?? st.Distance ?? 0;
            var segmentDistanceKm = distanceKm - metrics.TotalDistance;
            metrics.TotalDistance = distanceKm;

            var segmentTimeMs = st.SplitTimeMs ?? 0;
            cumulativeTimeMs += segmentTimeMs;

            var (calculatedPace, speed) = CalculatePaceAndSpeed(segmentDistanceKm, segmentTimeMs);
            var effectivePace = st.AveragePace ?? calculatedPace;

            var splitTimeInfo = new SplitTimeInfo
            {
                CheckpointId = checkpoint != null ? _encryptionService.Encrypt(checkpoint.Id.ToString()) : null,
                CheckpointName = checkpoint?.Name,
                Distance = $"{distanceKm} km",
                DistanceKm = distanceKm,
                SplitTime = TimeFormatter.FormatTimeSpan(segmentTimeMs),
                CumulativeTime = TimeFormatter.FormatTimeSpan(cumulativeTimeMs),
                Pace = effectivePace.HasValue ? TimeFormatter.FormatPace(effectivePace.Value) : null,
                PaceValue = effectivePace.HasValue ? Math.Round(effectivePace.Value, 2) : null,
                Speed = speed.HasValue ? Math.Round(speed.Value, 1) : null
            };

            var progressionInfo = new PaceProgressionInfo
            {
                Segment = GetPaceProgressionSegment(checkpoint?.Name, distanceKm),
                Pace = splitTimeInfo.Pace,
                PaceValue = splitTimeInfo.PaceValue,
                Speed = splitTimeInfo.Speed,
                SplitTime = splitTimeInfo.SplitTime
            };

            return (splitTimeInfo, progressionInfo, effectivePace, speed);
        }

        private static (decimal? pace, decimal? speed) CalculatePaceAndSpeed(decimal segmentDistanceKm, long segmentTimeMs)
        {
            if (segmentDistanceKm <= 0 || segmentTimeMs <= 0)
                return (null, null);

            // Pace: minutes per km
            var pace = segmentTimeMs / 60000m / segmentDistanceKm;
            // Speed: km per hour
            var speed = segmentDistanceKm / (segmentTimeMs / 3600000m);

            return (pace, speed);
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

            return $"{(int)distanceKm}K";
        }

        private static PerformanceOverview BuildPerformanceOverview(PerformanceMetrics metrics)
        {
            if (!metrics.HasData)
                return new PerformanceOverview();

            var avgPace = metrics.GetAveragePace();
            var avgSpeed = metrics.GetAverageSpeed();

            return new PerformanceOverview
            {
                AverageSpeed = avgSpeed.HasValue ? Math.Round(avgSpeed.Value, 2) : null,
                AveragePace = avgPace.HasValue ? TimeFormatter.FormatPace(avgPace.Value) : null,
                MaxSpeed = metrics.MaxSpeed.HasValue ? Math.Round(metrics.MaxSpeed.Value, 1) : null,
                BestPace = metrics.BestPaceValue.HasValue ? TimeFormatter.FormatPace(metrics.BestPaceValue.Value) : null
            };
        }
    }
}
