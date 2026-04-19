using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Runnatics.Data.EF.Converters
{
    public class UtcDateTimeValueConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeValueConverter() : base(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        { }
    }
}
