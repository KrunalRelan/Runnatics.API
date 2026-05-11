using AutoMapper;
using Runnatics.Models.Client.Helpers;
using Runnatics.Models.Client.Responses.Participants;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for calculating performance metrics from split times
    /// </summary>
    public class PerformanceMetricsBuilder(IMapper mapper)
    {
        private readonly IMapper _mapper = mapper;

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
            // GunTime of the first checkpoint (= time from gun to start line crossing).
            // All subsequent cumulative times are measured from this baseline.
            var startGunTimeMs = splitTimes[0].SplitTimeMs ?? 0L;
            bool isFirstCheckpoint = true;
            decimal? previousPace = null;
            long? previousSplitTimeMs = null;

            foreach (var st in splitTimes)
            {
                // Cumulative = elapsed since crossing the start line.
                // For the Start row itself we show its own GunTime (the gun-to-start offset).
                // For every subsequent row: GunTime[i] - GunTime[Start].
                long cumulativeMs = isFirstCheckpoint
                    ? startGunTimeMs
                    : (st.SplitTimeMs ?? 0L) - startGunTimeMs;
                isFirstCheckpoint = false;

                var (splitTimeInfo, progressionInfo, segmentPace, segmentSpeed) = ProcessSplitTime(st, metrics, cumulativeMs, previousSplitTimeMs);
                previousSplitTimeMs = st.SplitTimeMs;

                // Populate checkpoint ranks from pre-calculated values
                splitTimeInfo.OverallRank = st.Rank;
                splitTimeInfo.GenderRank = st.GenderRank;
                splitTimeInfo.CategoryRank = st.CategoryRank;

                // Populate pace change info
                var distanceKm = st.ToCheckpoint?.DistanceFromStart ?? st.Distance ?? 0;
                progressionInfo.DistanceKm = distanceKm;

                if (distanceKm == 0)
                {
                    progressionInfo.PaceChangeDirection = "none";
                }
                else if (!previousPace.HasValue)
                {
                    progressionInfo.PaceChangeDirection = "first";
                }
                else if (segmentPace.HasValue)
                {
                    if (segmentPace.Value < previousPace.Value)
                        progressionInfo.PaceChangeDirection = "improved";
                    else if (segmentPace.Value > previousPace.Value)
                        progressionInfo.PaceChangeDirection = "declined";
                    else
                        progressionInfo.PaceChangeDirection = "none";

                    if (previousPace.Value > 0)
                    {
                        progressionInfo.PaceChangePercent = Math.Round(
                            ((segmentPace.Value - previousPace.Value) / previousPace.Value) * 100, 1);
                    }
                }
                else
                {
                    progressionInfo.PaceChangeDirection = "none";
                }

                if (segmentPace.HasValue && distanceKm > 0)
                {
                    previousPace = segmentPace;
                }

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
            long cumulativeTimeMs,
            long? previousSplitTimeMs)
        {
            var checkpoint = st.ToCheckpoint;
            var distanceKm = checkpoint?.DistanceFromStart ?? st.Distance ?? 0;
            var segmentDistanceKm = distanceKm - metrics.TotalDistance;
            metrics.TotalDistance = distanceKm;

            // SplitTime = time for this segment only (between consecutive checkpoints).
            // Prefer stored SegmentTime; when absent, derive from consecutive SplitTimeMs values.
            // For the very first row (no previous), SplitTimeMs is the gun-to-start-line offset.
            long segmentTimeMs;
            if (st.SegmentTime.HasValue)
            {
                segmentTimeMs = st.SegmentTime.Value;
            }
            else if (previousSplitTimeMs.HasValue)
            {
                segmentTimeMs = (st.SplitTimeMs ?? 0L) - previousSplitTimeMs.Value;
            }
            else
            {
                segmentTimeMs = st.SplitTimeMs ?? 0L;
            }

            var (calculatedPace, speed) = CalculatePaceAndSpeed(segmentDistanceKm, segmentTimeMs);
            var effectivePace = st.AveragePace ?? calculatedPace;

            // Map base properties from entity via AutoMapper
            var splitTimeInfo = _mapper.Map<SplitTimeInfo>(st);

            // Set computed properties
            splitTimeInfo.SplitTime = TimeFormatter.FormatTimeSpan(segmentTimeMs);
            splitTimeInfo.CumulativeTime = TimeFormatter.FormatTimeSpan(cumulativeTimeMs);
            splitTimeInfo.Pace = effectivePace.HasValue ? TimeFormatter.FormatPace(effectivePace.Value) : null;
            splitTimeInfo.PaceValue = effectivePace.HasValue ? Math.Round(effectivePace.Value, 2) : null;
            splitTimeInfo.Speed = speed.HasValue ? Math.Round(speed.Value, 1) : null;

            // Map base properties from entity via AutoMapper
            var progressionInfo = _mapper.Map<PaceProgressionInfo>(st);

            // Set computed properties
            progressionInfo.Segment = GetPaceProgressionSegment(checkpoint?.Name, distanceKm);
            progressionInfo.Pace = splitTimeInfo.Pace;
            progressionInfo.PaceValue = splitTimeInfo.PaceValue;
            progressionInfo.Speed = splitTimeInfo.Speed;
            progressionInfo.SplitTime = splitTimeInfo.SplitTime;

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
