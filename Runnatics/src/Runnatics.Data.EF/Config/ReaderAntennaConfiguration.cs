using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class ReaderAntennaConfiguration : IEntityTypeConfiguration<ReaderAntenna>
    {
        public void Configure(EntityTypeBuilder<ReaderAntenna> builder)
        {
            builder.ToTable("ReaderAntennas");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId)
                .IsRequired();

            builder.Property(e => e.AntennaPort)
                .IsRequired();

            builder.Property(e => e.AntennaName)
                .HasMaxLength(100);

            builder.Property(e => e.TxPowerCdBm)
                .HasDefaultValue(3000)
                .IsRequired();

            builder.Property(e => e.RxSensitivityCdBm)
                .HasDefaultValue(-7000)
                .IsRequired();

            builder.Property(e => e.IsEnabled)
                .HasDefaultValue(true)
                .IsRequired();

            builder.Property(e => e.Position);

            // Indexes
            builder.HasIndex(e => new { e.ReaderDeviceId, e.AntennaPort })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ReaderAntennas_Device_Port");

            builder.HasIndex(e => e.CheckpointId)
                .HasDatabaseName("IX_ReaderAntennas_Checkpoint");

            // Relationships
            builder.HasOne(e => e.ReaderDevice)
                .WithMany(r => r.ReaderAntennas)
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
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
