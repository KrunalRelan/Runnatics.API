using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Configurations
{
    public class CertificateFieldConfiguration : IEntityTypeConfiguration<CertificateField>
    {
        public void Configure(EntityTypeBuilder<CertificateField> builder)
        {
            builder.ToTable("CertificateFields");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
             .ValueGeneratedOnAdd();

            builder.Property(e => e.FieldType)
                .IsRequired();

            builder.Property(e => e.Content)
                .HasMaxLength(1000);

            builder.Property(e => e.XCoordinate)
                .IsRequired();

            builder.Property(e => e.YCoordinate)
                .IsRequired();

            builder.Property(e => e.Font)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(e => e.FontSize)
                .IsRequired();

            builder.Property(e => e.FontColor)
                .IsRequired()
                .HasMaxLength(7);

            builder.Property(e => e.Alignment)
                .HasMaxLength(20);

            builder.Property(e => e.FontWeight)
                .HasMaxLength(20);

            builder.Property(e => e.FontStyle)
                .HasMaxLength(20);

            // Configure AuditProperties as owned type
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

            // Relationships
            builder.HasOne(e => e.CertificateTemplate)
                .WithMany(t => t.Fields)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
