using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class DeviceConfiguration : IEntityTypeConfiguration<Device>
    {
        public void Configure(EntityTypeBuilder<Device> builder)
        {
            builder.ToTable("Devices");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
             .ValueGeneratedOnAdd();

            // Properties
            builder.Property(e => e.Name)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.TenantId)
                .IsRequired();

            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedDate)
                   .HasColumnName("CreatedAt")
                   .HasDefaultValueSql("GETUTCDATE()")
                   .IsRequired();

                ap.Property(p => p.UpdatedDate)
                  .HasColumnName("UpdatedAt");

                ap.Property(p => p.CreatedBy)
                  .HasColumnName("CreatedBy");

                ap.Property(p => p.UpdatedBy)
                  .HasColumnName("UpdatedBy");

                ap.Property(p => p.IsActive)
                  .HasColumnName("IsActive")
                  .HasDefaultValue(true)
                  .IsRequired();

                ap.Property(p => p.IsDeleted)
                  .HasColumnName("IsDeleted")
                  .HasDefaultValue(false)
                  .IsRequired();
            });

        }
    }
}
