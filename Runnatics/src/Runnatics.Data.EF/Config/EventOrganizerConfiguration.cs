
namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.EventOrganizers;

    public class EventOrganizerConfiguration : IEntityTypeConfiguration<EventOrganizer>
    {
        public void Configure(EntityTypeBuilder<EventOrganizer> builder)
        {
            builder.ToTable("EventOrganizer");

            builder.HasKey(eo => eo.Id);

            builder.Property(eo => eo.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd();

            builder.Property(eo => eo.TenantId)
                .HasColumnName("TenantId")
                .IsRequired();

            builder.Property(eo => eo.Name)
                .HasColumnName("Name")
                .IsRequired()
                .HasMaxLength(255);

            builder.HasOne(eo => eo.Organization)
                .WithMany(e => e.EventOrganizers)
                .HasForeignKey(eo => eo.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

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