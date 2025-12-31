using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class CertificateTemplateConfiguration : IEntityTypeConfiguration<CertificateTemplate>
    {
        public void Configure(EntityTypeBuilder<CertificateTemplate> builder)
        {
            builder.ToTable("CertificateTemplates");

            builder.HasKey(ct => ct.Id);

            builder.Property(ct => ct.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd();

            builder.Property(ct => ct.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(ct => ct.RaceId)
                .HasColumnName("RaceId");

            builder.Property(ct => ct.Name)
                .HasColumnName("Name")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(ct => ct.Description)
                .HasColumnName("Description")
                .HasMaxLength(1000);

            builder.Property(ct => ct.BackgroundImageUrl)
                .HasColumnName("BackgroundImageUrl")
                .HasMaxLength(500);

            builder.Property(ct => ct.BackgroundImageData)
                .HasColumnName("BackgroundImageData");

            builder.Property(ct => ct.Width)
                .HasColumnName("Width")
                .IsRequired()
                .HasDefaultValue(1754);

            builder.Property(ct => ct.Height)
                .HasColumnName("Height")
                .IsRequired()
                .HasDefaultValue(1240);

            builder.Property(ct => ct.IsDefault)
                .HasColumnName("IsDefault")
                .IsRequired()
                .HasDefaultValue(false);

            //builder.Property(ct => ct.IsActive)
            //    .HasColumnName("IsActive")
            //    .IsRequired()
            //    .HasDefaultValue(true);

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
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });
            // Indexes
            builder.HasIndex(ct => ct.EventId);
            builder.HasIndex(ct => new { ct.EventId, ct.RaceId });
            builder.HasIndex(ct => new { ct.EventId, ct.IsDefault })
                .HasDatabaseName("IX_CertificateTemplates_EventId_IsDefault");

            // Foreign Key Relationships
            builder.HasOne(ct => ct.Event)
                .WithMany()
                .HasForeignKey(ct => ct.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(ct => ct.Race)
                .WithMany()
                .HasForeignKey(ct => ct.RaceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Navigation to Fields
            builder.HasMany(ct => ct.Fields)
                .WithOne(cf => cf.CertificateTemplate)
                .HasForeignKey(cf => cf.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
