using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class CertificateFieldConfiguration : IEntityTypeConfiguration<CertificateField>
    {
        public void Configure(EntityTypeBuilder<CertificateField> builder)
        {
            builder.ToTable("CertificateFields");

            builder.HasKey(cf => cf.Id);

            builder.Property(cf => cf.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd();

            builder.Property(cf => cf.TemplateId)
                .HasColumnName("TemplateId")
                .IsRequired();

            builder.Property(cf => cf.FieldType)
                .HasColumnName("FieldType")
                .IsRequired();

            builder.Property(cf => cf.Content)
                .HasColumnName("Content")
                .HasMaxLength(1000);

            builder.Property(cf => cf.XCoordinate)
                .HasColumnName("XCoordinate")
                .IsRequired();

            builder.Property(cf => cf.YCoordinate)
                .HasColumnName("YCoordinate")
                .IsRequired();

            builder.Property(cf => cf.Font)
                .HasColumnName("Font")
                .HasMaxLength(100)
                .IsRequired()
                .HasDefaultValue("Arial");

            builder.Property(cf => cf.FontSize)
                .HasColumnName("FontSize")
                .IsRequired()
                .HasDefaultValue(12);

            builder.Property(cf => cf.FontColor)
                .HasColumnName("FontColor")
                .HasMaxLength(7)
                .IsRequired()
                .HasDefaultValue("000000");

            builder.Property(cf => cf.Width)
                .HasColumnName("Width");

            builder.Property(cf => cf.Height)
                .HasColumnName("Height");

            builder.Property(cf => cf.Alignment)
                .HasColumnName("Alignment")
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("left");

            builder.Property(cf => cf.FontWeight)
                .HasColumnName("FontWeight")
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("normal");

            builder.Property(cf => cf.FontStyle)
                .HasColumnName("FontStyle")
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("normal");

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy");

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
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            // Indexes
            builder.HasIndex(cf => cf.TemplateId);

            // Foreign Key Relationship
            builder.HasOne(cf => cf.CertificateTemplate)
                .WithMany(ct => ct.Fields)
                .HasForeignKey(cf => cf.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
