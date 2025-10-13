namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ReadNormalizedConfiguration : IEntityTypeConfiguration<ReadNormalized>
    {
        public virtual void Configure(EntityTypeBuilder<ReadNormalized> builder)
        {
            builder.HasKey(e => new { e.EventId, e.ParticipantId, e.CheckpointId });

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.ParticipantId)
                .IsRequired();

            builder.Property(e => e.CheckpointId)
                .IsRequired();

            builder.Property(e => e.RawReadId);

            builder.Property(e => e.ChipTime)
                .IsRequired();

            builder.Property(e => e.GunTime);

            builder.Property(e => e.NetTime);

            builder.Property(e => e.IsManualEntry)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.ManualEntryReason)
                .HasMaxLength(500);

            builder.Property(e => e.CreatedByUserId);

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

            builder.HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.RawRead)
                .WithMany()
                .HasForeignKey(e => e.RawReadId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.ChipTime);
            
            builder.HasIndex(e => e.GunTime);
            
            builder.HasIndex(e => e.RawReadId);
        }
    }
}
