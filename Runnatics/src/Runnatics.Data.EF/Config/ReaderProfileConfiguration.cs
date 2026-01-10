using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class ReaderProfileConfiguration : IEntityTypeConfiguration<ReaderProfile>
    {
        public void Configure(EntityTypeBuilder<ReaderProfile> builder)
        {
            builder.ToTable("ReaderProfiles");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.ProfileName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Description)
                .HasMaxLength(500);

            builder.Property(e => e.ReaderMode)
                .HasMaxLength(50)
                .HasDefaultValue("AutoSetDenseReader")
                .IsRequired();

            builder.Property(e => e.SearchMode)
                .HasMaxLength(50)
                .HasDefaultValue("DualTarget")
                .IsRequired();

            builder.Property(e => e.Session)
                .HasDefaultValue((byte)2)
                .IsRequired();

            builder.Property(e => e.TagPopulation)
                .HasDefaultValue(32)
                .IsRequired();

            builder.Property(e => e.FilterDuplicateReadsMs)
                .HasDefaultValue(1000)
                .IsRequired();

            builder.Property(e => e.DefaultTxPowerCdBm)
                .HasDefaultValue(3000)
                .IsRequired();

            builder.Property(e => e.EnableAntennaHub)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.IsDefault)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.AdvancedSettingsJson)
                .HasColumnType("nvarchar(max)");

            // Indexes
            builder.HasIndex(e => e.ProfileName)
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ReaderProfiles_Name");

            builder.HasIndex(e => e.IsDefault)
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ReaderProfiles_Default");

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
