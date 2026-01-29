namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class SplitTimeConfiguration : IEntityTypeConfiguration<SplitTimes>
    {
        public virtual void Configure(EntityTypeBuilder<SplitTimes> builder)
        {
            builder.ToTable("SplitTimes");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                     .ValueGeneratedOnAdd()
                        .IsRequired();

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

            // Time measurements in milliseconds
            builder.Property(e => e.SplitTimeMs);  // Nullable

            builder.Property(e => e.SegmentTime);  // Nullable

            // REQUIRED: Segment definition columns
            builder.Property(e => e.FromCheckpointId)
                .IsRequired();

            builder.Property(e => e.ToCheckpointId)
                .IsRequired();

            builder.Property(e => e.CheckpointId);  // Nullable (optional, usually same as ToCheckpointId)

            builder.Property(e => e.ReadNormalizedId);
            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                .HasColumnName("IsDeleted")
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                  .HasColumnName("CreatedAt")
                  .HasDefaultValueSql("GETUTCDATE()")
                  .IsRequired();

                ap.Property(p => p.CreatedBy)
                  .HasColumnName("CreatedBy")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy)
                .HasColumnName("UpdatedBy");

                ap.Property(p => p.UpdatedDate)
                .HasColumnName("UpdatedAt");

                ap.Property(p => p.IsActive)
                .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.SplitTimes)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Participant)
                .WithMany(p => p.SplitTimes)
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
