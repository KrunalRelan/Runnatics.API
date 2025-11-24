namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
    {
        public void Configure(EntityTypeBuilder<ImportBatch> builder)
        {
            builder.ToTable("ImportBatches");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.TenantId)
                .HasColumnName("TenantId")
                .IsRequired();

            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.FileName)
                .HasColumnName("FileName")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.TotalRecords)
                .HasColumnName("TotalRows")
                .IsRequired();

            // builder.Property(e => e.SuccessCount)
            //     .HasColumnName("SuccessCount")
            //     .HasDefaultValue(0)
            //     .IsRequired();

            // builder.Property(e => e.ErrorCount)
            //     .HasColumnName("ErrorCount")
            //     .HasDefaultValue(0)
            //     .IsRequired();

            builder.Property(e => e.Status)
                .HasColumnName("Status")
                .HasMaxLength(20)
                .HasDefaultValue("Pending")
                .IsRequired();

            // builder.Property(e => e.UploadedAt)
            //     .HasColumnName("UploadedAt")
            //     .HasDefaultValueSql("GETUTCDATE()")
            //     .IsRequired();

            builder.Property(e => e.ProcessedAt)
                .HasColumnName("CompletedAt");

            builder.Property(e => e.ErrorLog)
                .HasColumnName("ErrorLog")
                .HasColumnType("nvarchar(max)");

            // Indexes
            builder.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_ImportBatches_EventId");

            builder.HasIndex(e => e.Status)
                .HasDatabaseName("IX_ImportBatches_Status");

            builder.HasIndex(e => e.TenantId)
                .HasDatabaseName("IX_ImportBatches_TenantId");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

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
