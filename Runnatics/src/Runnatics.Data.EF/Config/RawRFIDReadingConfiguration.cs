using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class RawRFIDReadingConfiguration : IEntityTypeConfiguration<RawRFIDReading>
    {
        public void Configure(EntityTypeBuilder<RawRFIDReading> builder)
        {
            builder.ToTable("RawRFIDReadings");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.BatchId)
                .IsRequired();

            builder.Property(e => e.DeviceId)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.Epc)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.TimestampMs)
                .IsRequired();

            builder.Property(e => e.Antenna);

            builder.Property(e => e.RssiDbm)
                .HasColumnType("decimal(5,2)");

            builder.Property(e => e.Channel);

            builder.Property(e => e.ReadTimeLocal)
                .IsRequired();

            builder.Property(e => e.ReadTimeUtc)
                .IsRequired();

            builder.Property(e => e.TimeZoneId)
                .HasMaxLength(50)
                .HasDefaultValue("UTC");

            builder.Property(e => e.ProcessResult)
                .HasMaxLength(20)
                .HasDefaultValue("Pending")
                .IsRequired();

            builder.Property(e => e.AssignmentMethod)
                .HasMaxLength(20);

            builder.Property(e => e.CheckpointConfidence)
                .HasColumnType("decimal(5,4)");

            builder.Property(e => e.IsMultipleEpc)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.RequiresManualReview)
                .HasDefaultValue(false);

            builder.Property(e => e.IsManualEntry)
                .HasDefaultValue(false);

            builder.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasDefaultValue("file_upload");

            builder.Property(e => e.Notes)
                .HasColumnType("nvarchar(max)");

            // Indexes
            builder.HasIndex(e => e.BatchId);
            builder.HasIndex(e => e.Epc);
            builder.HasIndex(e => e.ProcessResult);
            builder.HasIndex(e => new { e.Epc, e.TimestampMs });

            // Relationships
            builder.HasOne(e => e.UploadBatch)
                .WithMany(b => b.Readings)
                .HasForeignKey(e => e.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AuditProperties
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy).HasColumnName("CreatedBy");
                ap.Property(p => p.CreatedDate).HasColumnName("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
                ap.Property(p => p.UpdatedBy).HasColumnName("UpdatedBy");
                ap.Property(p => p.UpdatedDate).HasColumnName("UpdatedAt");
                ap.Property(p => p.IsDeleted).HasColumnName("IsDeleted").HasDefaultValue(false);
                ap.Property(p => p.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
            });
        }
    }
}
