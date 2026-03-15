namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Helper class for time formatting
    /// Follows Single Responsibility Principle - only handles time formatting
    /// </summary>
    public static class TimeFormatter
    {
        /// <summary>
        /// Formats milliseconds to time string (HH:mm:ss)
        /// </summary>
        public static string? FormatTimeSpan(long? milliseconds)
        {
            if (!milliseconds.HasValue)
                return null;

            var ts = TimeSpan.FromMilliseconds(milliseconds.Value);
            return ts.ToString(@"hh\:mm\:ss");
        }

        /// <summary>
        /// Formats pace value (decimal minutes per km) to string (m:ss/km)
        /// </summary>
        public static string FormatPace(decimal paceMinutesPerKm)
        {
            var totalSeconds = (int)(paceMinutesPerKm * 60);
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes}:{seconds:D2}/km";
        }
    }
}
