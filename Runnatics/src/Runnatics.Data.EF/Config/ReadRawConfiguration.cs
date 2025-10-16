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

                        builder.Property(e => e.Timestamp)
                                .IsRequired();

                        builder.Property(e => e.IsProcessed)
                                .HasDefaultValue(false);

                        builder.Property(e => e.CreatedAt)
                                .HasDefaultValueSql("GETUTCDATE()")
                                .IsRequired();

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

                        // Relationships
                        builder.HasOne(e => e.Event)
                            .WithMany(ev => ev.ReadRaws)
                            .HasForeignKey(e => e.EventId)
                            .OnDelete(DeleteBehavior.Cascade);

                        builder.HasOne(e => e.ReaderDevice)
                            .WithMany(rd => rd.ReadRaws)
                            .HasForeignKey(e => e.ReaderDeviceId)
                            .OnDelete(DeleteBehavior.Restrict);

                        builder.OwnsOne(o => o.AuditProperties, ap =>
                        {
                                ap.Property(p => p.CreatedBy)
                                .IsRequired();

                                ap.Property(p => p.CreatedDate)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

                                ap.Property(p => p.UpdatedBy);

                                ap.Property(p => p.UpdatedDate);

                                ap.Property(p => p.IsDeleted)
                .HasDefaultValue(false)
                .IsRequired();

                                ap.Property(p => p.IsActive)
                .HasDefaultValue(true)
                .IsRequired();
                        });
                }
        }
}
