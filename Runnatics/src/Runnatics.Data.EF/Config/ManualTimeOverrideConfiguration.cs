namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ManualTimeOverrideConfiguration : IEntityTypeConfiguration<ManualTimeOverride>
    {
        public virtual void Configure(EntityTypeBuilder<ManualTimeOverride> builder)
        {
            builder.ToTable("ManualTimeOverrides");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                   .ValueGeneratedOnAdd()
                   .IsRequired();

            builder.Property(e => e.EventId).IsRequired();
            builder.Property(e => e.RaceId).IsRequired();
            builder.Property(e => e.ParticipantId).IsRequired();
            builder.Property(e => e.CheckpointId).IsRequired();

            builder.Property(e => e.ManualCrossingUtc).IsRequired();

            builder.Property(e => e.Reason).HasMaxLength(500);

            builder.Property(e => e.CreatedByUserId);

            // Configure AuditProperties as owned entity (mirrors ReadNormalizedConfiguration)
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedDate")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedDate");

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            // Relationships — Restrict everywhere (no inverse collections needed on principals).
            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Race)
                .WithMany()
                .HasForeignKey(e => e.RaceId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // One ACTIVE override per (participant, checkpoint). Filtered so soft-deleted
            // rows release the slot (matches the hand-run SQL filtered unique index).
            builder.HasIndex(e => new { e.ParticipantId, e.CheckpointId })
                .HasFilter("[IsDeleted] = 0")
                .IsUnique();

            builder.HasIndex(e => new { e.EventId, e.RaceId });
        }
    }
}
