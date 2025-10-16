namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ReaderAssignmentConfiguration : IEntityTypeConfiguration<ReaderAssignment>
    {
        public virtual void Configure(EntityTypeBuilder<ReaderAssignment> builder)
        {
            builder.HasKey(e => new { e.EventId, e.CheckpointId, e.ReaderDeviceId });

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.CheckpointId)
                .IsRequired();

            builder.Property(e => e.ReaderDeviceId)
                .IsRequired();

            builder.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            builder.Property(e => e.AssignedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            builder.Property(e => e.UnassignedAt);

            builder.Property(e => e.AssignedByUserId);

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

                ap.Property(p => p.IsActive)
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.ReaderDevice)
                .WithMany(r => r.ReaderAssignments)
                .HasForeignKey(e => e.ReaderDeviceId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.AssignedByUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.EventId);
            
            builder.HasIndex(e => e.CheckpointId);

            builder.HasIndex(e => e.AssignedAt);

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
