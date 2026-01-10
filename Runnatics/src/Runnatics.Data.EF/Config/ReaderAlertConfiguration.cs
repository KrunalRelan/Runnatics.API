using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class ReaderAlertConfiguration : IEntityTypeConfiguration<ReaderAlert>
    {
        public void Configure(EntityTypeBuilder<ReaderAlert> builder)
        {
            builder.ToTable("ReaderAlerts");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId)
                .IsRequired();

            builder.Property(e => e.AlertType)
                .IsRequired();

            builder.Property(e => e.Severity)
                .HasDefaultValue(AlertSeverity.Warning)
                .IsRequired();

            builder.Property(e => e.Message)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(e => e.Details)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.IsAcknowledged)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.AcknowledgedByUserId);

            builder.Property(e => e.AcknowledgedAt);

            builder.Property(e => e.ResolutionNotes)
                .HasMaxLength(1000);

            builder.Property(e => e.IsResolved)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.ResolvedAt);

            // Indexes
            builder.HasIndex(e => e.ReaderDeviceId)
                .HasDatabaseName("IX_ReaderAlerts_Device");

            builder.HasIndex(e => new { e.IsAcknowledged, e.Severity })
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ReaderAlerts_Unacknowledged");

            builder.HasIndex(e => e.AlertType)
                .HasDatabaseName("IX_ReaderAlerts_Type");

            // Relationships
            builder.HasOne(e => e.ReaderDevice)
                .WithMany(r => r.ReaderAlerts)
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.AcknowledgedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

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
