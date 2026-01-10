using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class FileUploadMappingConfiguration : IEntityTypeConfiguration<FileUploadMapping>
    {
        public void Configure(EntityTypeBuilder<FileUploadMapping> builder)
        {
            builder.ToTable("FileUploadMappings");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.MappingName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.FileFormat)
                .IsRequired();

            builder.Property(e => e.Description)
                .HasMaxLength(500);

            builder.Property(e => e.HasHeaderRow)
                .HasDefaultValue(true)
                .IsRequired();

            builder.Property(e => e.Delimiter)
                .HasMaxLength(5)
                .HasDefaultValue(",")
                .IsRequired();

            builder.Property(e => e.EpcColumn)
                .HasMaxLength(50)
                .HasDefaultValue("epc")
                .IsRequired();

            builder.Property(e => e.TimestampColumn)
                .HasMaxLength(50)
                .HasDefaultValue("timestamp")
                .IsRequired();

            builder.Property(e => e.TimestampFormat)
                .HasMaxLength(100)
                .HasDefaultValue("yyyy-MM-ddTHH:mm:ss.fffZ")
                .IsRequired();

            builder.Property(e => e.AntennaPortColumn)
                .HasMaxLength(50);

            builder.Property(e => e.RssiColumn)
                .HasMaxLength(50);

            builder.Property(e => e.ReaderSerialColumn)
                .HasMaxLength(50);

            builder.Property(e => e.PhaseAngleColumn)
                .HasMaxLength(50);

            builder.Property(e => e.DopplerColumn)
                .HasMaxLength(50);

            builder.Property(e => e.ChannelIndexColumn)
                .HasMaxLength(50);

            builder.Property(e => e.TagSeenCountColumn)
                .HasMaxLength(50);

            builder.Property(e => e.LatitudeColumn)
                .HasMaxLength(50);

            builder.Property(e => e.LongitudeColumn)
                .HasMaxLength(50);

            builder.Property(e => e.IsDefault)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.AdditionalMappingsJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.OrganizationId);

            // Indexes
            builder.HasIndex(e => new { e.FileFormat, e.IsDefault })
                .HasDatabaseName("IX_FileUploadMappings_Format_Default");

            builder.HasIndex(e => e.OrganizationId)
                .HasDatabaseName("IX_FileUploadMappings_Organization");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Audit Properties
            builder.OwnsOne(o => o.AuditProperties, ap =>
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
                    .HasColumnName("CreatedBy");

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt");

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            });
        }
    }
}
