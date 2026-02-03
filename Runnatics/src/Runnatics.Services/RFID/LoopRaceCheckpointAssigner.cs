using Microsoft.Extensions.Logging;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Services.RFID
{
    /// <summary>
    /// Generic loop race checkpoint assignment using statistical clustering.
    /// Works for any race distance (5K, 10K, 21K, 42K) and any number of loops.
    /// </summary>
    public class LoopRaceCheckpointAssigner
    {
        private readonly ILogger _logger;

        public LoopRaceCheckpointAssigner(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Assigns readings to checkpoints for a shared device used at multiple checkpoints.
        /// Uses statistical clustering to find natural timing groups.
        /// </summary>
        /// <param name="allReadings">All readings from the shared device</param>
        /// <param name="checkpoints">Checkpoints in order by distance</param>
        /// <param name="raceStartTime">Race start time</param>
        /// <param name="raceDistance">Total race distance in kilometers</param>
        /// <returns>Split times for each checkpoint in seconds from race start</returns>
        public List<double> CalculateSplitTimes(
            List<DateTime> allReadings,
            List<Checkpoint> checkpoints,
            DateTime raceStartTime,
            decimal raceDistance)
        {
            if (checkpoints.Count < 2)
            {
                throw new ArgumentException("Need at least 2 checkpoints for loop race assignment");
            }

            // =================================================================
            // FIX: Deduplicate readings within time window before analysis
            // This prevents duplicate reads from skewing the clustering
            // =================================================================
            var dedupWindowSeconds = 30.0;
            var sortedReadings = allReadings.OrderBy(r => r).ToList();
            var deduplicatedReadings = new List<DateTime>();

            if (sortedReadings.Count > 0)
            {
                deduplicatedReadings.Add(sortedReadings[0]);
                for (int i = 1; i < sortedReadings.Count; i++)
                {
                    var gap = (sortedReadings[i] - deduplicatedReadings.Last()).TotalSeconds;
                    if (gap > dedupWindowSeconds)
                    {
                        deduplicatedReadings.Add(sortedReadings[i]);
                    }
                }
            }

            if (deduplicatedReadings.Count != allReadings.Count)
            {
                _logger.LogInformation(
                    "Split time analysis: Deduplicated {Original} readings to {Deduped} unique passes",
                    allReadings.Count, deduplicatedReadings.Count);
            }

            // Use deduplicated readings for the rest of the analysis
            // Convert readings to elapsed seconds from race start
            var elapsedTimes = deduplicatedReadings
                .Select(r => (r - raceStartTime).TotalSeconds)
                .Where(t => t >= -60) // Allow 1 minute pre-race grace period
                .OrderBy(t => t)
                .ToList();

            if (elapsedTimes.Count == 0)
            {
                throw new InvalidOperationException("No valid readings found for timing analysis");
            }

            _logger.LogInformation(
                "Analyzing {Count} readings ({DeduplicatedCount} after dedup) for {CheckpointCount} checkpoints over {Distance}km race",
                allReadings.Count, elapsedTimes.Count, checkpoints.Count, raceDistance);

            // Strategy 1: Detect natural gaps in the timeline (most reliable)
            var splitsByGapDetection = DetectTimingGaps(elapsedTimes, checkpoints.Count);

            // Strategy 2: Use expected timing based on distance (fallback)
            var splitsByDistance = CalculateExpectedSplits(checkpoints, raceDistance, elapsedTimes);

            // Strategy 3: Use clustering algorithm (advanced fallback)
            var splitsByClustering = ClusterReadingsByTime(elapsedTimes, checkpoints.Count);

            // Choose the best strategy based on confidence metrics
            var finalSplits = SelectBestStrategy(
                splitsByGapDetection,
                splitsByDistance,
                splitsByClustering,
                elapsedTimes,
                checkpoints);

            LogSplitResults(finalSplits, checkpoints);

            return finalSplits;
        }

        /// <summary>
        /// Strategy 1: Detect natural timing gaps between checkpoint waves.
        /// Works best when there's clear separation between checkpoint crossing times.
        /// </summary>
        private (List<double> splits, double confidence) DetectTimingGaps(
            List<double> elapsedTimes,
            int checkpointCount)
        {
            var splits = new List<double>();
            var gaps = new List<(double time, double gap, int index)>();

            // Find all gaps between consecutive readings
            for (int i = 1; i < elapsedTimes.Count; i++)
            {
                var gap = elapsedTimes[i] - elapsedTimes[i - 1];
                var midpoint = (elapsedTimes[i] + elapsedTimes[i - 1]) / 2;
                gaps.Add((midpoint, gap, i));
            }

            if (gaps.Count == 0)
            {
                return (splits, 0.0);
            }

            // Sort by gap size (largest first)
            var largestGaps = gaps.OrderByDescending(g => g.gap).Take(checkpointCount - 1).ToList();

            // Confidence: ratio of largest gap to median gap
            var sortedGaps = gaps.Select(g => g.gap).OrderBy(g => g).ToList();
            var medianGap = sortedGaps[sortedGaps.Count / 2];
            var maxGap = largestGaps.Count > 0 ? largestGaps.Max(g => g.gap) : 0;
            var confidence = medianGap > 0 ? maxGap / medianGap : 0.0;

            // Sort split times chronologically
            splits = largestGaps.OrderBy(g => g.time).Select(g => g.time).ToList();

            _logger.LogDebug(
                "Gap Detection: Found {Count} splits with confidence {Confidence:F2}. Max gap: {MaxGap}s, Median gap: {MedianGap}s",
                splits.Count, confidence, maxGap, medianGap);

            return (splits, confidence);
        }

        /// <summary>
        /// Strategy 2: Calculate expected split times based on checkpoint distances.
        /// Uses observed data to estimate pace, then projects checkpoint times.
        /// </summary>
        private (List<double> splits, double confidence) CalculateExpectedSplits(
            List<Checkpoint> checkpoints,
            decimal raceDistance,
            List<double> elapsedTimes)
        {
            var splits = new List<double>();

            if (raceDistance <= 0 || elapsedTimes.Count == 0)
            {
                return (splits, 0.0);
            }

            // Estimate average finishing time from late readings (75th percentile)
            var estimatedFinishTime = elapsedTimes.Count > 10
                ? elapsedTimes[elapsedTimes.Count * 3 / 4]
                : elapsedTimes.Last();

            // Calculate implied pace (seconds per kilometer)
            var impliedPaceSecPerKm = estimatedFinishTime / (double)raceDistance;

            // Generate expected checkpoint times based on distance
            for (int i = 0; i < checkpoints.Count - 1; i++)
            {
                var checkpoint = checkpoints[i];
                var nextCheckpoint = checkpoints[i + 1];
                var segmentDistance = nextCheckpoint.DistanceFromStart - checkpoint.DistanceFromStart;

                // Expected time at midpoint between checkpoints
                var midpointDistance = checkpoint.DistanceFromStart + (segmentDistance / 2);
                var expectedTime = (double)midpointDistance * impliedPaceSecPerKm;

                splits.Add(expectedTime);
            }

            // Confidence: lower for distance-based estimation (it's a fallback)
            var confidence = 0.5;

            _logger.LogDebug(
                "Distance-based: Estimated pace {Pace:F1}min/km, finish time {Time:F0}s. Generated {Count} splits",
                impliedPaceSecPerKm / 60, estimatedFinishTime, splits.Count);

            return (splits, confidence);
        }

        /// <summary>
        /// Strategy 3: K-means clustering to find natural groupings in the timeline.
        /// Advanced fallback for complex timing patterns.
        /// </summary>
        private (List<double> splits, double confidence) ClusterReadingsByTime(
            List<double> elapsedTimes,
            int checkpointCount)
        {
            var splits = new List<double>();

            if (elapsedTimes.Count < checkpointCount)
            {
                return (splits, 0.0);
            }

            // Simple k-means implementation for 1D data
            var centroids = InitializeCentroids(elapsedTimes, checkpointCount);
            var maxIterations = 20;
            var convergenceThreshold = 1.0; // seconds

            for (int iter = 0; iter < maxIterations; iter++)
            {
                var assignments = AssignToClusters(elapsedTimes, centroids);
                var newCentroids = RecalculateCentroids(elapsedTimes, assignments, checkpointCount);

                // Check convergence
                var maxChange = centroids.Zip(newCentroids, (old, newC) => Math.Abs(old - newC)).Max();
                centroids = newCentroids;

                if (maxChange < convergenceThreshold)
                {
                    _logger.LogDebug("K-means converged after {Iterations} iterations", iter + 1);
                    break;
                }
            }

            // Sort centroids to ensure chronological order
            centroids = centroids.OrderBy(c => c).ToList();

            // Split times are midpoints between adjacent cluster centroids
            for (int i = 0; i < centroids.Count - 1; i++)
            {
                splits.Add((centroids[i] + centroids[i + 1]) / 2);
            }

            // Confidence: based on cluster separation (silhouette-like metric)
            var assignments2 = AssignToClusters(elapsedTimes, centroids);
            var confidence = CalculateClusterSeparation(elapsedTimes, assignments2, centroids);

            _logger.LogDebug(
                "K-means Clustering: {Count} splits with confidence {Confidence:F2}",
                splits.Count, confidence);

            return (splits, confidence);
        }

        /// <summary>
        /// Select the best strategy based on confidence metrics and data quality.
        /// </summary>
        private List<double> SelectBestStrategy(
            (List<double> splits, double confidence) gapDetection,
            (List<double> splits, double confidence) distanceBased,
            (List<double> splits, double confidence) clustering,
            List<double> elapsedTimes,
            List<Checkpoint> checkpoints)
        {
            var strategies = new[]
            {
                ("Gap Detection", gapDetection.splits, gapDetection.confidence),
                ("Distance-Based", distanceBased.splits, distanceBased.confidence),
                ("Clustering", clustering.splits, clustering.confidence)
            };

            // Apply heuristics to adjust confidence scores

            // Gap detection works best with clear separation (confidence > 3.0 means 3x larger gap)
            if (gapDetection.confidence > 3.0 && gapDetection.splits.Count == checkpoints.Count - 1)
            {
                _logger.LogInformation("Using Gap Detection strategy (high confidence: {Confidence:F2})",
                    gapDetection.confidence);
                return gapDetection.splits;
            }

            // Clustering works well with large sample sizes
            if (elapsedTimes.Count > 200 && clustering.confidence > 0.6 && clustering.splits.Count == checkpoints.Count - 1)
            {
                _logger.LogInformation("Using Clustering strategy (large sample: {Count} readings)",
                    elapsedTimes.Count);
                return clustering.splits;
            }

            // Filter strategies with correct split count
            var validStrategies = strategies
                .Where(s => s.splits.Count == checkpoints.Count - 1)
                .ToList();

            if (validStrategies.Count == 0)
            {
                // Fall back to distance-based if no strategy produced correct split count
                _logger.LogWarning("No strategy produced valid splits. Using distance-based fallback.");
                return distanceBased.splits;
            }

            // Choose strategy with highest confidence
            var best = validStrategies.OrderByDescending(s => s.confidence).First();

            _logger.LogInformation(
                "Using {Strategy} strategy (confidence: {Confidence:F2})",
                best.Item1, best.confidence);

            return best.splits;
        }

        /// <summary>
        /// Initialize k-means centroids evenly spaced across the timeline.
        /// </summary>
        private List<double> InitializeCentroids(List<double> times, int k)
        {
            var min = times.Min();
            var max = times.Max();
            var step = (max - min) / k;

            var centroids = new List<double>();
            for (int i = 0; i < k; i++)
            {
                centroids.Add(min + step * (i + 0.5));
            }

            return centroids;
        }

        /// <summary>
        /// Assign each time to the nearest centroid.
        /// </summary>
        private List<int> AssignToClusters(List<double> times, List<double> centroids)
        {
            return times.Select(t =>
            {
                var distances = centroids.Select((c, i) => (distance: Math.Abs(t - c), index: i));
                return distances.OrderBy(d => d.distance).First().index;
            }).ToList();
        }

        /// <summary>
        /// Recalculate centroids as the mean of assigned points.
        /// </summary>
        private List<double> RecalculateCentroids(
            List<double> times,
            List<int> assignments,
            int k)
        {
            var centroids = new List<double>();

            for (int i = 0; i < k; i++)
            {
                var clusterPoints = times.Where((t, idx) => assignments[idx] == i).ToList();
                centroids.Add(clusterPoints.Count > 0 ? clusterPoints.Average() : 0);
            }

            return centroids;
        }

        /// <summary>
        /// Calculate cluster separation quality (0 to 1, higher is better).
        /// </summary>
        private double CalculateClusterSeparation(
            List<double> times,
            List<int> assignments,
            List<double> centroids)
        {
            if (centroids.Count < 2) return 0;

            // Calculate average within-cluster distance
            var withinClusterDist = 0.0;
            for (int i = 0; i < times.Count; i++)
            {
                withinClusterDist += Math.Abs(times[i] - centroids[assignments[i]]);
            }
            withinClusterDist /= times.Count;

            // Calculate minimum between-cluster distance
            var betweenClusterDist = double.MaxValue;
            for (int i = 0; i < centroids.Count - 1; i++)
            {
                var dist = Math.Abs(centroids[i + 1] - centroids[i]);
                betweenClusterDist = Math.Min(betweenClusterDist, dist);
            }

            // Silhouette-like score: (between - within) / max(between, within)
            var score = (betweenClusterDist - withinClusterDist) / Math.Max(betweenClusterDist, withinClusterDist);
            return Math.Max(0, Math.Min(1, score));
        }

        /// <summary>
        /// Log the final split results for debugging.
        /// </summary>
        private void LogSplitResults(List<double> splits, List<Checkpoint> checkpoints)
        {
            _logger.LogInformation("Final split times calculated:");

            for (int i = 0; i < splits.Count && i < checkpoints.Count - 1; i++)
            {
                var fromCheckpoint = checkpoints[i];
                var toCheckpoint = checkpoints[i + 1];
                var splitTimeMinutes = splits[i] / 60;

                _logger.LogInformation(
                    "  Split {Index}: {Time:F1}min ({Seconds:F0}s) - Between '{From}' ({FromDist}km) and '{To}' ({ToDist}km)",
                    i + 1,
                    splitTimeMinutes,
                    splits[i],
                    fromCheckpoint.Name ?? "Start",
                    fromCheckpoint.DistanceFromStart,
                    toCheckpoint.Name ?? "Unknown",
                    toCheckpoint.DistanceFromStart);
            }
        }

        /// <summary>
        /// Assign a specific participant's readings to checkpoints using calculated splits.
        /// </summary>
        public List<(DateTime reading, int checkpointIndex)> AssignParticipantReadings(
            List<DateTime> participantReadings,
            List<double> splitTimes,
            DateTime raceStartTime)
        {
            var assignments = new List<(DateTime reading, int checkpointIndex)>();

            foreach (var reading in participantReadings.OrderBy(r => r))
            {
                var elapsedSeconds = (reading - raceStartTime).TotalSeconds;

                // Find which checkpoint this reading belongs to
                int checkpointIndex = 0;
                for (int i = 0; i < splitTimes.Count; i++)
                {
                    if (elapsedSeconds <= splitTimes[i])
                    {
                        break;
                    }
                    checkpointIndex = i + 1;
                }

                assignments.Add((reading, checkpointIndex));
            }

            return assignments;
        }
    }
}