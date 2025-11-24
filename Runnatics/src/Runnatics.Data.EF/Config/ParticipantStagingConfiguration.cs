namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ParticipantStagingConfiguration : IEntityTypeConfiguration<ParticipantStaging>
    {
        public void Configure(EntityTypeBuilder<ParticipantStaging> builder)
        {
            builder.ToTable("ParticipantStaging");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ImportBatchId)
                .HasColumnName("ImportBatchId")
                .IsRequired();

            builder.Property(e => e.RowNumber)
                .HasColumnName("RowNumber")
                .IsRequired();

            builder.Property(e => e.Bib)
                .HasColumnName("Bib")
                .HasMaxLength(50);

            builder.Property(e => e.FirstName)
                .HasColumnName("FirstName")
                .HasMaxLength(500);

            builder.Property(e => e.Gender)
                .HasColumnName("Gender")
                .HasMaxLength(50);

            builder.Property(e => e.AgeCategory)
                .HasColumnName("AgeCategory")
                .HasMaxLength(100);

            builder.Property(e => e.Email)
                .HasColumnName("Email")
                .HasMaxLength(255);

            builder.Property(e => e.Mobile)
                .HasColumnName("Mobile")
                .HasMaxLength(50);

            builder.Property(e => e.ProcessingStatus)
                .HasColumnName("ProcessingStatus")
                .HasMaxLength(20)
                .HasDefaultValue("Pending")
                .IsRequired();

            builder.Property(e => e.ErrorMessage)
                .HasColumnName("ErrorMessage")
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.ParticipantId)
                .HasColumnName("ParticipantId");

            // Indexes
            builder.HasIndex(e => e.ImportBatchId)
                .HasDatabaseName("IX_ParticipantStaging_ImportBatchId");

            builder.HasIndex(e => e.ProcessingStatus)
                .HasDatabaseName("IX_ParticipantStaging_ProcessingStatus");

            // Relationships
            builder.HasOne(e => e.ImportBatch)
                .WithMany(ib => ib.StagingRecords)
                .HasForeignKey(e => e.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy")
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt");

                ap.Property(p => p.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .HasDefaultValue(false);

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });
        }
    }
}
