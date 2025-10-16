using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class RaceCategoryConfiguration : IEntityTypeConfiguration<RaceCategory>
    {
        public void Configure(EntityTypeBuilder<RaceCategory> builder)
        {
            builder.ToTable("RaceCategories");

            // Properties
            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.Name)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.DistanceKm)
                .HasColumnType("decimal(6,3)")
                .IsRequired();

            builder.Property(e => e.StartTime)
                .IsRequired();

            builder.Property(e => e.EntryFee)
                .HasColumnType("decimal(10,2)");

            builder.Property(e => e.AgeMin)
                .HasDefaultValue(0);

            builder.Property(e => e.AgeMax)
                .HasDefaultValue(120);

            builder.Property(e => e.GenderRestriction)
                .HasMaxLength(20);

            // Indexes
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_RaceCategories_EventId");

            builder.HasIndex(e => new { e.EventId, e.Name })
                .HasDatabaseName("IX_RaceCategories_EventId_Name");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.RaceCategories)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(e => e.Participants)
                .WithOne(p => p.RaceCategory)
                .HasForeignKey(p => p.RaceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

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