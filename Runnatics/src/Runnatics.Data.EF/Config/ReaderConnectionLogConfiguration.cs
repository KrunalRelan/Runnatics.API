using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class ReaderConnectionLogConfiguration : IEntityTypeConfiguration<ReaderConnectionLog>
    {
        public void Configure(EntityTypeBuilder<ReaderConnectionLog> builder)
        {
            builder.ToTable("ReaderConnectionLogs");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId)
                .IsRequired();

            builder.Property(e => e.EventType)
                .IsRequired();

            builder.Property(e => e.ConnectionProtocol);

            builder.Property(e => e.IpAddress)
                .HasMaxLength(45);

            builder.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            builder.Property(e => e.Timestamp)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            // Indexes
            builder.HasIndex(e => new { e.ReaderDeviceId, e.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_ReaderConnectionLogs_Device_Timestamp");

            builder.HasIndex(e => e.Timestamp)
                .IsDescending()
                .HasDatabaseName("IX_ReaderConnectionLogs_Timestamp");

            // Relationships
            builder.HasOne(e => e.ReaderDevice)
                .WithMany(r => r.ReaderConnectionLogs)
                .HasForeignKey(e => e.ReaderDeviceId)
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
