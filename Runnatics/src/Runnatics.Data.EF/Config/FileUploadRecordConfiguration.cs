using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class FileUploadRecordConfiguration : IEntityTypeConfiguration<FileUploadRecord>
    {
        public void Configure(EntityTypeBuilder<FileUploadRecord> builder)
        {
            builder.ToTable("FileUploadRecords");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.FileUploadBatchId)
                .IsRequired();

            builder.Property(e => e.RowNumber)
                .IsRequired();

            builder.Property(e => e.Epc)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(e => e.ReadTimestamp)
                .IsRequired();

            builder.Property(e => e.AntennaPort);

            builder.Property(e => e.RssiDbm)
                .HasColumnType("decimal(6,2)");

            builder.Property(e => e.ReaderSerialNumber)
                .HasMaxLength(100);

            builder.Property(e => e.ReaderHostname)
                .HasMaxLength(100);

            builder.Property(e => e.PhaseAngleDegrees)
                .HasColumnType("decimal(6,2)");

            builder.Property(e => e.DopplerFrequencyHz)
                .HasColumnType("decimal(10,2)");

            builder.Property(e => e.ChannelIndex);

            builder.Property(e => e.PeakRssiCdBm);

            builder.Property(e => e.TagSeenCount)
                .HasDefaultValue(1)
                .IsRequired();

            builder.Property(e => e.GpsLatitude)
                .HasColumnType("decimal(10,7)");

            builder.Property(e => e.GpsLongitude)
                .HasColumnType("decimal(10,7)");

            builder.Property(e => e.ProcessingStatus)
                .HasDefaultValue(ReadRecordStatus.Pending)
                .IsRequired();

            builder.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            builder.Property(e => e.MatchedChipId);

            builder.Property(e => e.MatchedParticipantId);

            builder.Property(e => e.CreatedReadRawId);

            builder.Property(e => e.RawData)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.ProcessedAt);

            // Indexes
            builder.HasIndex(e => e.FileUploadBatchId)
                .HasDatabaseName("IX_FileUploadRecords_Batch");

            builder.HasIndex(e => e.Epc)
                .HasDatabaseName("IX_FileUploadRecords_Epc");

            builder.HasIndex(e => e.ProcessingStatus)
                .HasDatabaseName("IX_FileUploadRecords_Status");

            builder.HasIndex(e => new { e.FileUploadBatchId, e.ProcessingStatus })
                .HasDatabaseName("IX_FileUploadRecords_Batch_Status");

            builder.HasIndex(e => new { e.Epc, e.ReadTimestamp })
                .HasDatabaseName("IX_FileUploadRecords_Epc_Timestamp");

            // Relationships
            builder.HasOne(e => e.FileUploadBatch)
                .WithMany(b => b.FileUploadRecords)
                .HasForeignKey(e => e.FileUploadBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.MatchedChip)
                .WithMany()
                .HasForeignKey(e => e.MatchedChipId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.MatchedParticipant)
                .WithMany()
                .HasForeignKey(e => e.MatchedParticipantId)
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
