using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class ReadingCheckpointAssignmentConfiguration : IEntityTypeConfiguration<ReadingCheckpointAssignment>
    {
        public void Configure(EntityTypeBuilder<ReadingCheckpointAssignment> builder)
        {
            builder.ToTable("ReadingCheckpointAssignments");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.ReadingId)
                .IsRequired();

            builder.Property(e => e.CheckpointId)
                .IsRequired();

            // Indexes
            builder.HasIndex(e => e.ReadingId);
            builder.HasIndex(e => e.CheckpointId);
            builder.HasIndex(e => new { e.ReadingId, e.CheckpointId })
                .IsUnique();

            // Relationships
            builder.HasOne(e => e.Reading)
                .WithMany(r => r.ReadingCheckpointAssignments)
                .HasForeignKey(e => e.ReadingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Checkpoint)
                .WithMany()
                .HasForeignKey(e => e.CheckpointId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure AuditProperties
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy).HasColumnName("CreatedBy");
                ap.Property(p => p.CreatedDate).HasColumnName("CreatedAt").HasDefaultValueSql("GETUTCDATE()");
                ap.Property(p => p.UpdatedBy).HasColumnName("UpdatedBy");
                ap.Property(p => p.UpdatedDate).HasColumnName("UpdatedAt");
                ap.Property(p => p.IsDeleted).HasColumnName("IsDeleted").HasDefaultValue(false);
                ap.Property(p => p.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
            });
        }
    }
}
