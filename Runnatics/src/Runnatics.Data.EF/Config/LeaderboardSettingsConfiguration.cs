namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class LeaderboardSettingsConfiguration : IEntityTypeConfiguration<LeaderboardSettings>
    {
        public void Configure(EntityTypeBuilder<LeaderboardSettings> builder)
        {
            builder.ToTable("LeaderboardSettings");

            builder.HasKey(e => e.Id);
            
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            // Properties
            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.ShowOverallResults)
                .HasColumnName("ShowOverallResults")
                .HasDefaultValue(true);

            builder.Property(e => e.ShowCategoryResults)
                .HasColumnName("ShowCategoryResults")
                .HasDefaultValue(true);

            builder.Property(e => e.ShowGenderResults)
                .HasColumnName("ShowGenderResults")
                .HasDefaultValue(true);

            builder.Property(e => e.ShowAgeGroupResults)
                .HasColumnName("ShowAgeGroupResults")
                .HasDefaultValue(true);

            builder.Property(e => e.SortByOverallChipTime)
                .HasColumnName("SortByOverallChipTime")
                .HasDefaultValue(false);

            builder.Property(e => e.SortByOverallGunTime)
                .HasColumnName("SortByOverallGunTime")
                .HasDefaultValue(false);

            builder.Property(e => e.SortByCategoryChipTime)
                .HasColumnName("SortByCategoryChipTime")
                .HasDefaultValue(false);

            builder.Property(e => e.SortByCategoryGunTime)
                .HasColumnName("SortByCategoryGunTime")
                .HasDefaultValue(false);

            builder.Property(e => e.EnableLiveLeaderboard)
                .HasColumnName("EnableLiveLeaderboard")
                .HasDefaultValue(true);

            builder.Property(e => e.NumberOfResultsToShowCategory)
                .HasColumnName("NumberOfResultsToShowCategory");

            builder.Property(e => e.NumberOfResultsToShowOverall)
                .HasColumnName("NumberOfResultsToShowOverall");

            builder.Property(e => e.ShowSplitTimes)
                .HasColumnName("ShowSplitTimes")
                .HasDefaultValue(true);

            builder.Property(e => e.ShowPace)
                .HasColumnName("ShowPace")
                .HasDefaultValue(true);

            builder.Property(e => e.ShowTeamResults)
                .HasColumnName("ShowTeamResults")
                .HasDefaultValue(false);

            builder.Property(e => e.ShowMedalIcon)
                .HasColumnName("ShowMedalIcon")
                .HasDefaultValue(true);

            builder.Property(e => e.AllowAnonymousView)
                .HasColumnName("AllowAnonymousView")
                .HasDefaultValue(true);

            builder.Property(e => e.AutoRefreshIntervalSec)
                .HasColumnName("AutoRefreshIntervalSec")
                .HasDefaultValue(30);

            builder.Property(e => e.MaxDisplayedRecords)
                .HasColumnName("MaxDisplayedRecords")
                .HasDefaultValue(100);

            // Indexes
            builder.HasIndex(e => e.EventId)
                .IsUnique()
                .HasDatabaseName("IX_LeaderboardSettings_EventId");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithOne(ev => ev.LeaderboardSettings)
                .HasForeignKey<LeaderboardSettings>(e => e.EventId)
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
            });
        }
    }
}
