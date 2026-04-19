using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Runnatics.Data.EF.Converters
{
    public class NullableUtcDateTimeValueConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeValueConverter() : base(
            v => v.HasValue
                ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : (DateTime?)null,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null)
        { }
    }
}
