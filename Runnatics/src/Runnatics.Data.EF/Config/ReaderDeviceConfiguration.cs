namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ReaderDeviceConfiguration : IEntityTypeConfiguration<ReaderDevice>
    {
        public virtual void Configure(EntityTypeBuilder<ReaderDevice> builder)
        {
            builder.ToTable("ReaderDevices");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                   .ValueGeneratedOnAdd()
                   .IsRequired();

            builder.Property(e => e.TenantId)
                .IsRequired();

            builder.Property(e => e.SerialNumber)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Model)
                .HasMaxLength(100);

            builder.Property(e => e.IpAddress)
                .HasMaxLength(45);

            builder.Property(e => e.MacAddress)
                .HasMaxLength(17);

            builder.Property(e => e.Hostname)
                .HasMaxLength(100);

            builder.Property(e => e.FirmwareVersion)
                .HasMaxLength(50);

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Offline")
                .IsRequired();

            builder.Property(e => e.LastHeartbeat);

            builder.Property(e => e.PowerLevel);

            builder.Property(e => e.AntennaCount)
                .HasDefaultValue(4)
                .IsRequired();

            builder.Property(e => e.Notes)
                .HasColumnType("nvarchar(max)");

            // New columns
            builder.Property(e => e.ConnectionType);

            builder.Property(e => e.LlrpPort);

            builder.Property(e => e.RestApiPort);

            builder.Property(e => e.Username)
                .HasMaxLength(100);

            builder.Property(e => e.PasswordHash)
                .HasMaxLength(255);

            builder.Property(e => e.ReaderModel)
                .HasMaxLength(50);

            builder.Property(e => e.ProfileId);

            builder.Property(e => e.CheckpointId);

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Profile)
                .WithMany(p => p.ReaderDevices)
                .HasForeignKey(e => e.ProfileId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.SerialNumber)
                .IsUnique();

            builder.HasIndex(e => e.MacAddress);

            builder.HasIndex(e => e.TenantId);

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
