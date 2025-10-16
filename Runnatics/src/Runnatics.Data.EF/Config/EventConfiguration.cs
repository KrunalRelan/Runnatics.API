using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities; // Ensure this namespace contains the Event class

namespace Runnatics.Data.EF.Config
{
    public class EventConfiguration : IEntityTypeConfiguration<Event>
    {
        public void Configure(EntityTypeBuilder<Event> builder)
        {
            builder.ToTable("Events");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
             .ValueGeneratedOnAdd();

            // Properties
            builder.Property(e => e.OrganizationId)
                .IsRequired();

            builder.Property(e => e.Name)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Slug)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Description)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.EventDate)
                .IsRequired();

            builder.Property(e => e.TimeZone)
                .HasMaxLength(50)
                .HasDefaultValue("Asia/Kolkata");

            builder.Property(e => e.VenueName)
                .HasMaxLength(255);

            builder.Property(e => e.VenueAddress)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.VenueLatitude)
                .HasColumnType("decimal(10,8)");

            builder.Property(e => e.VenueLongitude)
                .HasColumnType("decimal(11,8)");

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Draft");

            builder.Property(e => e.Settings)
                .HasColumnType("nvarchar(max)"); // JSON

            // Indexes
            builder.HasIndex(e => new { e.OrganizationId, e.Status })
                .HasDatabaseName("IX_Events_OrganizationId_Status");

            builder.HasIndex(e => new { e.OrganizationId, e.Slug })
                .IsUnique()
                .HasDatabaseName("IX_Events_OrganizationId_Slug");

            builder.HasIndex(e => e.EventDate)
                .HasDatabaseName("IX_Events_EventDate");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Events)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(e => e.RaceCategories)
                .WithOne(rc => rc.Event)
                .HasForeignKey(rc => rc.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(e => e.Participants)
                .WithOne(p => p.Event)
                .HasForeignKey(p => p.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(e => e.Checkpoints)
                .WithOne(c => c.Event)
                .HasForeignKey(c => c.EventId)
                .OnDelete(DeleteBehavior.Cascade);

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