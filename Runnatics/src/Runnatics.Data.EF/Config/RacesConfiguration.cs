using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class RaceConfiguration : IEntityTypeConfiguration<Race>
    {
        public void Configure(EntityTypeBuilder<Race> builder)
        {
            builder.ToTable("Races");

            builder.HasKey(e => e.Id);
  
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.Title)
                .HasColumnName("Title")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Description)
                .HasColumnName("Description")
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.Distance)
                .HasColumnName("Distance")
                .HasColumnType("decimal(10,2)");

            builder.Property(e => e.StartTime)
                .HasColumnName("StartTime")
                .HasColumnType("datetime2(7)");

            builder.Property(e => e.EndTime)
                .HasColumnName("EndTime")
                .HasColumnType("datetime2(7)");

            builder.Property(e => e.MaxParticipants)
                .HasColumnName("MaxParticipants");

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy");

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt");

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
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_Races_EventId");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.Races)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(e => e.Participants)
                .WithOne(p => p.Race)
                .HasForeignKey(p => p.RaceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasMany(e => e.Results)
                .WithOne(r => r.Race)
                .HasForeignKey(r => r.RaceId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}