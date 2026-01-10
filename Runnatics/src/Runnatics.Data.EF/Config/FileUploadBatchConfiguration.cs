using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class FileUploadBatchConfiguration : IEntityTypeConfiguration<FileUploadBatch>
    {
        public void Configure(EntityTypeBuilder<FileUploadBatch> builder)
        {
            builder.ToTable("FileUploadBatches");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.BatchGuid)
                .HasDefaultValueSql("NEWID()")
                .IsRequired();

            builder.Property(e => e.RaceId)
                .IsRequired();

            builder.Property(e => e.EventId);

            builder.Property(e => e.ReaderDeviceId);

            builder.Property(e => e.CheckpointId);

            builder.Property(e => e.Description)
                .HasMaxLength(500);

            builder.Property(e => e.OriginalFileName)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.StoredFileName)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.FileSizeBytes)
                .IsRequired();

            builder.Property(e => e.FileFormat)
                .IsRequired();

            builder.Property(e => e.ProcessingStatus)
                .HasDefaultValue(FileProcessingStatus.Pending)
                .IsRequired();

            builder.Property(e => e.ProcessingStartedAt);

            builder.Property(e => e.ProcessingCompletedAt);

            builder.Property(e => e.TotalRecords)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(e => e.ProcessedRecords)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(e => e.MatchedRecords)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(e => e.DuplicateRecords)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(e => e.ErrorRecords)
                .HasDefaultValue(0)
                .IsRequired();

            builder.Property(e => e.ErrorMessage)
                .HasMaxLength(2000);

            builder.Property(e => e.ProcessingLog)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.FileHash)
                .HasMaxLength(64);

            builder.Property(e => e.UploadedByUserId)
                .IsRequired();

            // Indexes
            builder.HasIndex(e => e.BatchGuid)
                .IsUnique()
                .HasDatabaseName("IX_FileUploadBatches_BatchGuid");

            builder.HasIndex(e => e.RaceId)
                .HasDatabaseName("IX_FileUploadBatches_Race");

            builder.HasIndex(e => e.ProcessingStatus)
                .HasDatabaseName("IX_FileUploadBatches_Status");

            builder.HasIndex(e => e.FileHash)
                .HasDatabaseName("IX_FileUploadBatches_Hash");

            builder.HasIndex(e => new { e.RaceId, e.ProcessingStatus })
                .HasDatabaseName("IX_FileUploadBatches_Race_Status");

            // Relationships
            builder.HasOne(e => e.Race)
                .WithMany()
                .HasForeignKey(e => e.RaceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.ReaderDevice)
                .WithMany()
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.UploadedByUser)
                .WithMany()
                .HasForeignKey(e => e.UploadedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

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
