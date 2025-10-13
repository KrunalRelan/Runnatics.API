namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;
    public class ChipConfiguration : IEntityTypeConfiguration<Chip>
    {
        public void Configure(EntityTypeBuilder<Chip> builder)
        {
            builder.ToTable("Participants");

            // Properties
            builder.ToTable("Chips");

            // Properties
            builder.Property(e => e.OrganizationId)
                .IsRequired();

            builder.Property(e => e.EPC)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Available");

            builder.Property(e => e.Notes)
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
        }
    }
}