namespace Runnatics.Data.EF.Config
{
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Runnatics.Models.Data.Entities;
        public class ReadRawConfiguration : IEntityTypeConfiguration<ReadRaw>
        {
                public void Configure(EntityTypeBuilder<ReadRaw> builder)
                {
                        builder.ToTable("ReadRaws");

                        // Primary Key
                        builder.HasKey(e => e.Id);

                        builder.Property(e => e.Id)
                            .ValueGeneratedOnAdd();

                        // Properties
                        builder.Property(e => e.EventId)
                                .IsRequired();

                        builder.Property(e => e.ReaderDeviceId)
                                .IsRequired();

                        builder.Property(e => e.ChipEPC)
                                .HasMaxLength(50)
                                .IsRequired();

                        builder.Property(e => e.Epc)
                                .HasMaxLength(64);

                        builder.Property(e => e.ReadTimestamp);

                        builder.Property(e => e.Timestamp)
                                .IsRequired();

                        builder.Property(e => e.Rssi);

                        builder.Property(e => e.AntennaPort);

                        builder.Property(e => e.CheckpointId);

                        builder.Property(e => e.IsProcessed)
                                .HasDefaultValue(false);

                        builder.Property(e => e.CreatedAt)
                                .HasDefaultValueSql("GETUTCDATE()")
                                .IsRequired();

                        // File upload and source tracking
                        builder.Property(e => e.FileUploadBatchId);

                        builder.Property(e => e.Source)
                                .HasMaxLength(50)
                                .HasDefaultValue("realtime");

                        // RFID signal properties
                        builder.Property(e => e.PhaseAngleDegrees)
                                .HasColumnType("decimal(6,2)");

                        builder.Property(e => e.DopplerFrequencyHz)
                                .HasColumnType("decimal(10,2)");

                        builder.Property(e => e.ChannelIndex);

                        builder.Property(e => e.TagSeenCount)
                                .HasDefaultValue(1);

                        // High-performance indexes for timing data
                        builder.HasIndex(e => new
                        {
                                e.EventId,
                                e.Timestamp
                        })
                                .HasDatabaseName("IX_ReadRaws_EventId_Timestamp");

                        builder.HasIndex(e => new
                        {
                                e.ChipEPC,
                                e.Timestamp
                        })
                                    .HasDatabaseName("IX_ReadRaws_ChipEPC_Timestamp");

                        builder.HasIndex(e => e.IsProcessed)
                            .HasDatabaseName("IX_ReadRaws_IsProcessed")
                            .HasFilter("[IsProcessed] = 0");

                        builder.HasIndex(e => e.FileUploadBatchId)
                            .HasDatabaseName("IX_ReadRaws_FileUploadBatch");

                        builder.HasIndex(e => e.Source)
                            .HasDatabaseName("IX_ReadRaws_Source");

                        // Relationships
                        builder.HasOne(e => e.Event)
                            .WithMany(ev => ev.ReadRaws)
                            .HasForeignKey(e => e.EventId)
                            .OnDelete(DeleteBehavior.Cascade);

                        builder.HasOne(e => e.ReaderDevice)
                            .WithMany(rd => rd.ReadRaws)
                            .HasForeignKey(e => e.ReaderDeviceId)
                            .OnDelete(DeleteBehavior.Restrict);

                        builder.HasOne(e => e.FileUploadBatch)
                            .WithMany()
                            .HasForeignKey(e => e.FileUploadBatchId)
                            .OnDelete(DeleteBehavior.SetNull);

                        builder.OwnsOne(o => o.AuditProperties, ap =>
                        {
                                ap.Property(p => p.CreatedBy)
                                  .HasColumnName("CreatedBy");

                                ap.Property(p => p.CreatedDate)
                                  .HasColumnName("CreatedAt")
                                  .HasDefaultValueSql("GETUTCDATE()")
                                  .IsRequired();

                                ap.Property(p => p.UpdatedBy)
                                  .HasColumnName("UpdatedBy");

                                ap.Property(p => p.UpdatedDate)
                                    .HasColumnName("UpdatedAt");

                                ap.Property(p => p.IsDeleted)
                                    .HasColumnName("IsDeleted")
                                    .HasDefaultValue(false)
                                    .IsRequired();

                                ap.Property(p => p.IsActive)
                                    .HasColumnName("IsActive")
                                    .HasDefaultValue(true)
                                    .IsRequired();
                        });
                }
        }
}
