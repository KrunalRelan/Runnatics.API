using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class ReaderHealthStatusConfiguration : IEntityTypeConfiguration<ReaderHealthStatus>
    {
        public void Configure(EntityTypeBuilder<ReaderHealthStatus> builder)
        {
            builder.ToTable("ReaderHealthStatuses");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId)
                .IsRequired();

            builder.Property(e => e.IsOnline)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.LastHeartbeat);

            builder.Property(e => e.CpuTemperatureCelsius)
                .HasColumnType("decimal(5,2)");

            builder.Property(e => e.AmbientTemperatureCelsius)
                .HasColumnType("decimal(5,2)");

            builder.Property(e => e.ReaderMode)
                .HasDefaultValue(ReaderMode.Offline)
                .IsRequired();

            builder.Property(e => e.FirmwareVersion)
                .HasMaxLength(50);

            builder.Property(e => e.TotalReadsToday)
                .HasDefaultValue(0L)
                .IsRequired();

            builder.Property(e => e.LastReadTimestamp);

            builder.Property(e => e.UptimeSeconds);

            builder.Property(e => e.MemoryUsagePercent)
                .HasColumnType("decimal(5,2)");

            builder.Property(e => e.CpuUsagePercent)
                .HasColumnType("decimal(5,2)");

            // Indexes
            builder.HasIndex(e => e.ReaderDeviceId)
                .IsUnique()
                .HasDatabaseName("IX_ReaderHealthStatuses_Device");

            builder.HasIndex(e => e.IsOnline)
                .HasDatabaseName("IX_ReaderHealthStatuses_Online");

            // Relationships
            builder.HasOne(e => e.ReaderDevice)
                .WithOne(r => r.HealthStatus)
                .HasForeignKey<ReaderHealthStatus>(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Audit Properties
            builder.OwnsOne(o => o.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy");

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt");

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });
        }
    }
}
