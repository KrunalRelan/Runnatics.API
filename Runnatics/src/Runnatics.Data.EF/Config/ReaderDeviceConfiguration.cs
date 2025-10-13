namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ReaderDeviceConfiguration : IEntityTypeConfiguration<ReaderDevice>
    {
        public virtual void Configure(EntityTypeBuilder<ReaderDevice> builder)
        {
            builder.HasKey(e => new { e.OrganizationId, e.SerialNumber });

            builder.Property(e => e.OrganizationId)
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

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.CreatedBy)
                    .IsRequired();

                ap.Property(p => p.UpdatedBy);

                ap.Property(p => p.UpdatedDate);

                ap.Property(p => p.IsActive)
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            builder.HasIndex(e => e.SerialNumber)
                .IsUnique();

            builder.HasIndex(e => e.MacAddress);
            
            builder.HasIndex(e => e.OrganizationId);
        }
    }
}
