
namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.EventOrganizers;

    public class EventOrganizerConfiguration : IEntityTypeConfiguration<EventOrganizer>
    {
        public void Configure(EntityTypeBuilder<EventOrganizer> builder)
        {
            builder.ToTable("EventOrganizers");

            builder.HasKey(eo => eo.Id);

            builder.Property(eo => eo.OrganizerName)
                .IsRequired()
                .HasMaxLength(255);

            builder.HasOne(eo => eo.Organization)
                .WithMany(e => e.EventOrganizers)
                .HasForeignKey(eo => eo.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

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