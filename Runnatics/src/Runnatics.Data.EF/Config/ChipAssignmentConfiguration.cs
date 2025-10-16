namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ChipAssignmentConfiguration : IEntityTypeConfiguration<ChipAssignment>
    {
        public void Configure(EntityTypeBuilder<ChipAssignment> builder)
        {
            builder.ToTable("ChipAssignments");
            
            // Composite primary key
            builder.HasKey(e => new { e.EventId, e.ParticipantId, e.ChipId });

            // Properties
            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.ParticipantId)
                .HasColumnName("ParticipantId")
                .IsRequired();

            builder.Property(e => e.ChipId)
                .HasColumnName("ChipId")
                .IsRequired();

            builder.Property(e => e.AssignedAt)
                .HasColumnName("AssignedAt")
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            builder.Property(e => e.UnassignedAt)
                .HasColumnName("UnassignedAt");

            builder.Property(e => e.AssignedByUserId)
                .HasColumnName("AssignedByUserId");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.ChipAssignments)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Participant)
                .WithMany(p => p.ChipAssignments)
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Chip)
                .WithMany(c => c.ChipAssignments)
                .HasForeignKey(e => e.ChipId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.AssignedByUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
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

            // Indexes
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_ChipAssignments_EventId");

            builder.HasIndex(e => e.ParticipantId)
                .HasDatabaseName("IX_ChipAssignments_ParticipantId");

            builder.HasIndex(e => e.ChipId)
                .HasDatabaseName("IX_ChipAssignments_ChipId");

            builder.HasIndex(e => e.AssignedAt)
                .HasDatabaseName("IX_ChipAssignments_AssignedAt");
        }
    }
}
