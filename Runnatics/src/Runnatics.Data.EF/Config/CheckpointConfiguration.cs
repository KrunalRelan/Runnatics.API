namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;   

    public class CheckpointConfiguration : IEntityTypeConfiguration<Checkpoint>
    {
        public void Configure(EntityTypeBuilder<Checkpoint> builder)
        {
            builder.ToTable("Checkpoints");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
             .ValueGeneratedOnAdd();
            // Properties
            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.Name)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Type)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(e => e.DistanceKm)
                .HasColumnType("decimal(6,3)")
                .IsRequired();

            builder.Property(e => e.Latitude)
                .HasColumnType("decimal(10,8)");

            builder.Property(e => e.Longitude)
                .HasColumnType("decimal(11,8)");

            builder.Property(e => e.MinGapMs)
                .HasDefaultValue(1000);

            builder.Property(e => e.SortOrder)
                .HasColumnName("SortOrder");

            // Indexes
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_Checkpoints_EventId");

            builder.HasIndex(e => new { e.EventId, e.Type })
                .HasDatabaseName("IX_Checkpoints_EventId_Type");

            builder.HasIndex(e => new { e.EventId, e.DistanceKm })
                .HasDatabaseName("IX_Checkpoints_EventId_DistanceKm");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.Checkpoints)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                    .HasDefaultValue(false)
                    .IsRequired();
                ap.Property(p => p.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();
                ap.Property(p => p.CreatedBy)
                    .IsRequired();
                ap.Property(p => p.UpdatedBy);
                ap.Property(p => p.UpdatedDate);

                builder.Property(e => e.IsActive)
               .HasColumnName("IsActive")
               .HasDefaultValue(true)
               .IsRequired();
            });
        }
    }
}