using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class UploadBatchConfiguration : IEntityTypeConfiguration<UploadBatch>
    {
        public void Configure(EntityTypeBuilder<UploadBatch> builder)
        {
            builder.ToTable("UploadBatches");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            // RaceId is optional for event-level uploads
            builder.Property(e => e.RaceId)
                .IsRequired(false);

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.DeviceId)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.OriginalFileName)
                .HasMaxLength(255);

            builder.Property(e => e.StoredFilePath)
                .HasMaxLength(500);

            builder.Property(e => e.FileHash)
                .HasMaxLength(50);

            builder.Property(e => e.FileFormat)
                .HasMaxLength(20)
                .HasDefaultValue("DB");

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("uploading");

            builder.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasDefaultValue("file_upload");

            builder.Property(e => e.IsLiveSync)
                .HasDefaultValue(false);

            // Indexes - Individual indexes for foreign keys
            builder.HasIndex(e => e.RaceId);
            builder.HasIndex(e => e.EventId);
            builder.HasIndex(e => e.FileHash);
            builder.HasIndex(e => e.Status);

            // Composite index for pending batch queries (EventId + RaceId + Status)
            // This optimizes the common query pattern: WHERE EventId = X AND RaceId = Y AND Status IN ('uploaded', 'uploading')
            builder.HasIndex(e => new { e.EventId, e.RaceId, e.Status })
                .HasDatabaseName("IX_UploadBatches_EventId_RaceId_Status");

            // Relationships
            // Race is optional - event-level uploads may have RaceId = NULL
            builder.HasOne(e => e.Race)
                .WithMany()
                .HasForeignKey(e => e.RaceId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.ReaderDevice)
                .WithMany()
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.ExpectedCheckpoint)
                .WithMany()
                .HasForeignKey(e => e.ExpectedCheckpointId)
                .OnDelete(DeleteBehavior.SetNull);

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
