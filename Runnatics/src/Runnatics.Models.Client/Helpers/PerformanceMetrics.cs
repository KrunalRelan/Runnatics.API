namespace Runnatics.Models.Client.Helpers
{
    /// <summary>
    /// Tracks and calculates performance metrics during split time processing
    /// </summary>
    public sealed class PerformanceMetrics
    {
        private decimal _totalPace;
        private decimal _totalSpeed;
        private int _count;

        public decimal TotalDistance { get; set; }
        public decimal? BestPaceValue { get; private set; }
        public decimal? MaxSpeed { get; private set; }

        public void UpdateMetrics(decimal paceValue, decimal speedValue)
        {
            _totalPace += paceValue;
            _totalSpeed += speedValue;
            _count++;

            if (!BestPaceValue.HasValue || paceValue < BestPaceValue)
                BestPaceValue = paceValue;

            if (!MaxSpeed.HasValue || speedValue > MaxSpeed)
                MaxSpeed = speedValue;
        }

        public decimal? GetAveragePace() => _count > 0 ? _totalPace / _count : null;
        public decimal? GetAverageSpeed() => _count > 0 ? _totalSpeed / _count : null;
        public bool HasData => _count > 0;
    }
}
