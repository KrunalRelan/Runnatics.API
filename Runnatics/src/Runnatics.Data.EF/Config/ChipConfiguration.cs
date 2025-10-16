namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;
    public class ChipConfiguration : IEntityTypeConfiguration<Chip>
    {
        public void Configure(EntityTypeBuilder<Chip> builder)
        {
            builder.ToTable("Chips");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            // Properties
            builder.Property(e => e.OrganizationId)
                .HasColumnName("OrganizationId")
                .IsRequired();

            builder.Property(e => e.EPC)
                .HasColumnName("EPC")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.Status)
                .HasColumnName("Status")
                .HasMaxLength(20)
                .HasDefaultValue("Available");

            builder.Property(e => e.BatteryLevel)
                .HasColumnName("BatteryLevel");

            builder.Property(e => e.LastSeenAt)
                .HasColumnName("LastSeenAt");

            builder.Property(e => e.Notes)
                .HasColumnName("Notes")
                .HasColumnType("nvarchar(max)");

            // Indexes
            builder.HasIndex(e => e.EPC)
                .IsUnique()
                .HasDatabaseName("IX_Chips_EPC");

            builder.HasIndex(e => new { e.OrganizationId, e.Status })
                .HasDatabaseName("IX_Chips_OrganizationId_Status");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Chips)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.OwnsOne(o => o.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy")
                    .HasMaxLength(100)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy")
                    .HasMaxLength(100);

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