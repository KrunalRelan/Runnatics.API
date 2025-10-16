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

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                   .HasColumnName("Id")
                   .ValueGeneratedOnAdd()
                   .IsRequired();
                   
            // Properties
            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.Name)
                .HasColumnName("Name")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.DistanceKm)
                .HasColumnName("DistanceKm")
                .HasColumnType("decimal(6,3)")
                .IsRequired();

            builder.Property(e => e.StartTime)
                .HasColumnName("StartTime")
                .IsRequired();

            builder.Property(e => e.CutoffTime)
                .HasColumnName("CutoffTime");

            builder.Property(e => e.MaxParticipants)
                .HasColumnName("MaxParticipants");

            builder.Property(e => e.EntryFee)
                .HasColumnName("EntryFee")
                .HasColumnType("decimal(10,2)");

            builder.Property(e => e.AgeMin)
                .HasColumnName("AgeMin")
                .HasDefaultValue(0);

            builder.Property(e => e.AgeMax)
                .HasColumnName("AgeMax")
                .HasDefaultValue(120);

            builder.Property(e => e.GenderRestriction)
                .HasColumnName("GenderRestriction")
                .HasMaxLength(20);

            builder.Property(e => e.IsActive)
                .HasColumnName("IsActive")
                .HasDefaultValue(true);

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