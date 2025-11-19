using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class RaceSettingsConfiguration : IEntityTypeConfiguration<RaceSettings>
    {
        public void Configure(EntityTypeBuilder<RaceSettings> builder)
        {
            builder.ToTable("RaceSettings");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                    .HasColumnName("Id")
                    .ValueGeneratedOnAdd()
                    .IsRequired();

            // Properties
            builder.Property(e => e.RaceId)
                     .HasColumnName("RaceId")
                     .IsRequired();

            builder.Property(e => e.Published)
                    .HasColumnName("Published")
                    .HasDefaultValue(false)
                    .IsRequired();

            builder.Property(e => e.SendSms)
                   .HasColumnName("SendSms")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.CheckValidation)
                   .HasColumnName("CheckValidation")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.ShowLeaderboard)
                   .HasColumnName("ShowLeaderboard")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.ShowResultTable)
                   .HasColumnName("ShowResultTable")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.IsTimed)
                   .HasColumnName("IsTimed")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.DedUpSeconds)
                   .HasColumnName("DedUpSeconds");

            builder.Property(e => e.EarlyStartCutOff)
                   .HasColumnName("EarlyStartCutOff");

            builder.Property(e => e.LateStartCutOff)
                   .HasColumnName("LateStartCutOff");

            builder.Property(e => e.HasLoops)
                   .HasColumnName("HasLoops");

            builder.Property(e => e.LoopLength)
                   .HasColumnName("LoopLength");

            builder.Property(e => e.DataHeaders)
                   .HasColumnName("DataHeader")
                   .HasColumnType("nvarchar(max)");

            //// Indexes
            //builder.HasIndex(e => e.RaceId)
            //    .IsUnique()
            //    .HasDatabaseName("IX_EventSettings_EventId");

            // Relationships
            builder.HasOne(e => e.Race)
                .WithOne(ev => ev.RaceSettings)
                .HasForeignKey<RaceSettings>(e => e.RaceId)
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
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();
            }
            );
        }
    }
}
