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

            builder.Property(e => e.RaceId)
                .IsRequired();

            builder.Property(e => e.Name)
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(e => e.DistanceFromStart)
                .HasColumnType("decimal(6,3)")
                .IsRequired();

            builder.Property(e => e.DeviceId)
                .IsRequired();

            builder.Property(e => e.ParentDeviceId);

            builder.Property(e => e.IsMandatory)
                .IsRequired();

            // Indexes
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_Checkpoints_EventId");           

            builder.HasIndex(e => new { e.EventId, e.DistanceFromStart })
                .HasDatabaseName("IX_Checkpoints_EventId_DistanceKm");

            // Relationships
            // Map relationships to the explicit navigation properties to avoid creating shadow foreign keys
            builder.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.ParentDevice)
                .WithMany()
                .HasForeignKey(e => e.ParentDeviceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure AuditProperties as owned entity
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