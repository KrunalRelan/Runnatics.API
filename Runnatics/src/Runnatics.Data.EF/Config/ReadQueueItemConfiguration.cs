using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class ReadQueueItemConfiguration : IEntityTypeConfiguration<ReadQueueItem>
    {
        public void Configure(EntityTypeBuilder<ReadQueueItem> builder)
        {
            builder.ToTable("ReadQueue");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.Epc)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId);

            builder.Property(e => e.AntennaPort);

            builder.Property(e => e.ReadTimestamp)
                .IsRequired();

            builder.Property(e => e.RssiDbm)
                .HasColumnType("decimal(6,2)");

            builder.Property(e => e.RaceId);

            builder.Property(e => e.CheckpointId);

            builder.Property(e => e.Source)
                .HasMaxLength(50)
                .HasDefaultValue("realtime")
                .IsRequired();

            builder.Property(e => e.FileUploadBatchId);

            builder.Property(e => e.ProcessingStatus)
                .HasDefaultValue(ReadRecordStatus.Pending)
                .IsRequired();

            builder.Property(e => e.ProcessedAt);

            builder.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            builder.Property(e => e.RetryCount)
                .HasDefaultValue((byte)0)
                .IsRequired();

            builder.Property(e => e.MaxRetries)
                .HasDefaultValue((byte)3)
                .IsRequired();

            builder.Property(e => e.Priority)
                .HasDefaultValue((byte)5)
                .IsRequired();

            // Indexes
            builder.HasIndex(e => new { e.ProcessingStatus, e.Priority, e.Id })
                .IsDescending(false, true, false)
                .HasDatabaseName("IX_ReadQueue_Processing");

            builder.HasIndex(e => e.Epc)
                .HasDatabaseName("IX_ReadQueue_Epc");

            builder.HasIndex(e => e.ReadTimestamp)
                .HasDatabaseName("IX_ReadQueue_Timestamp");

            builder.HasIndex(e => e.RaceId)
                .HasDatabaseName("IX_ReadQueue_Race");

            // Relationships
            builder.HasOne(e => e.ReaderDevice)
                .WithMany()
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.Race)
                .WithMany()
                .HasForeignKey(e => e.RaceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.FileUploadBatch)
                .WithMany(b => b.ReadQueueItems)
                .HasForeignKey(e => e.FileUploadBatchId)
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
