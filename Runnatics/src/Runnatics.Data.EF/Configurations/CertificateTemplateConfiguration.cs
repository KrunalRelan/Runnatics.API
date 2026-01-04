using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Configurations
{
    public class CertificateTemplateConfiguration : IEntityTypeConfiguration<CertificateTemplate>
    {
        public void Configure(EntityTypeBuilder<CertificateTemplate> builder)
        {
            builder.ToTable("CertificateTemplates");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
             .ValueGeneratedOnAdd();

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.RaceId);

            builder.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            builder.Property(e => e.Description)
                .HasMaxLength(1000);

            builder.Property(e => e.BackgroundImageUrl)
                .HasMaxLength(500);

            builder.Property(e => e.Width)
                .IsRequired();

            builder.Property(e => e.Height)
                .IsRequired();

            //builder.Property(e => e.IsActive)
            //    .IsRequired();

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
            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Race)
                .WithMany()
                .HasForeignKey(e => e.RaceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            builder.HasMany(e => e.Fields)
                .WithOne(f => f.CertificateTemplate)
                .HasForeignKey(f => f.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
