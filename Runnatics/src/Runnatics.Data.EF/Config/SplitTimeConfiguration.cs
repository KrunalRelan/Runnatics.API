namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class SplitTimeConfiguration : IEntityTypeConfiguration<SplitTime>
    {
        public virtual void Configure(EntityTypeBuilder<SplitTime> builder)
        {
            builder.HasKey(e => new { e.EventId, e.ParticipantId, e.CheckpointId });

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.ParticipantId)
                .IsRequired();

            builder.Property(e => e.CheckpointId)
                .IsRequired();

            builder.Property(e => e.ReadNormalizedId);

            builder.Property(e => e.SplitTimeMs)
                .IsRequired();

            builder.Property(e => e.SegmentTime);

            builder.Property(e => e.Pace)
                .HasColumnType("decimal(10,3)");

            builder.Property(e => e.Rank);

            builder.Property(e => e.GenderRank);

            builder.Property(e => e.CategoryRank);

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

            builder.HasOne(e => e.ReadNormalized)
                .WithMany()
                .HasForeignKey(e => e.ReadNormalizedId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.SplitTimeMs);
            
            builder.HasIndex(e => e.Rank);

            builder.HasIndex(e => e.ReadNormalizedId);
            
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
