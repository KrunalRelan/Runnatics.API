using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;
using Runnatics.Models.Data.Enumerations;

namespace Runnatics.Data.EF.Config
{
    public class EventConfiguration : IEntityTypeConfiguration<Event>
    {
        public void Configure(EntityTypeBuilder<Event> builder)
        {
            builder.ToTable("Events");

            builder.HasKey(e => e.Id);

            // Map to actual database columns
            builder.Property(e => e.Id)
                   .HasColumnName("Id")
                           .ValueGeneratedOnAdd();

            builder.Property(e => e.OrganizationId)
                  .HasColumnName("OrganizationId")
           .IsRequired();

            builder.Property(e => e.Name)
            .HasColumnName("Name")
                        .HasMaxLength(510)
                     .IsRequired();

            builder.Property(e => e.Slug)
   .HasColumnName("Slug")
      .HasMaxLength(200)
                .IsRequired();

            builder.Property(e => e.Description)
         .HasColumnName("Description")
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.EventDate)
            .HasColumnName("EventDate")
         .IsRequired();

            builder.Property(e => e.TimeZone)
              .HasColumnName("TimeZone")
              .HasMaxLength(100)
               .HasDefaultValue("Asia/Kolkata");

            builder.Property(e => e.VenueName)
      .HasColumnName("VenueName")
       .HasMaxLength(510);

            builder.Property(e => e.VenueAddress)
 .HasColumnName("VenueAddress")
      .HasColumnType("nvarchar(max)");

            builder.Property(e => e.VenueLatitude)
      .HasColumnName("VenueLatitude")
       .HasColumnType("decimal(10,8)");

            builder.Property(e => e.VenueLongitude)
                 .HasColumnName("VenueLongitude")
             .HasColumnType("decimal(11,8)");

            builder.Property(e => e.Status)
              .HasColumnName("Status")
              .HasMaxLength(40)
              .HasConversion<string>()
              .HasDefaultValue(EventStatus.Draft);

            builder.Property(e => e.MaxParticipants)
          .HasColumnName("MaxParticipants");

            builder.Property(e => e.RegistrationDeadline)
              .HasColumnName("RegistrationDeadline");

            builder.Property(e => e.Settings)
            .HasColumnName("Settings")
         .HasColumnType("nvarchar(max)"); // JSON            // Configure AuditProperties to match your database schema
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

            // Indexes
            builder.HasIndex(e => new { e.OrganizationId, e.Status });
            builder.HasIndex(e => new { e.OrganizationId, e.Slug })
             .IsUnique();
            builder.HasIndex(e => e.EventDate);
        }
    }
}