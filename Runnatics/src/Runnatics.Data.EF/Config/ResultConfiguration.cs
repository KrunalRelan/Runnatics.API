namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;
    public class ResultConfiguration : IEntityTypeConfiguration<Results>
    {
        public void Configure(EntityTypeBuilder<Results> builder)
        {
            builder.ToTable("Results");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                     .ValueGeneratedOnAdd()
                        .IsRequired();

            // Properties
            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.ParticipantId)
                .IsRequired();

            builder.Property(e => e.RaceCategoryId)
                .IsRequired();

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Finished");

            builder.Property(e => e.DisqualificationReason)
                .HasMaxLength(255);

            builder.Property(e => e.IsOfficial)
                .HasDefaultValue(false);

            builder.Property(e => e.CertificateGenerated)
                .HasDefaultValue(false);

            // Computed Properties
            builder.Ignore(e => e.FinishTimeSpan);
            builder.Ignore(e => e.FormattedFinishTime);

            // Indexes for performance
            builder.HasIndex(e => new { e.EventId, e.OverallRank })
                .HasDatabaseName("IX_Results_EventId_OverallRank");

            builder.HasIndex(e => new { e.EventId, e.GenderRank })
                .HasDatabaseName("IX_Results_EventId_GenderRank");

            builder.HasIndex(e => new { e.EventId, e.CategoryRank })
                .HasDatabaseName("IX_Results_EventId_CategoryRank");

            builder.HasIndex(e => e.ParticipantId)
                .IsUnique()
                .HasDatabaseName("IX_Results_ParticipantId");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.Results)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Participant)
                .WithOne(p => p.Result)
                .HasForeignKey<Results>(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.RaceCategory)
                .WithMany(rc => rc.Results)
                .HasForeignKey(e => e.RaceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(o => o.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy);

                ap.Property(p => p.UpdatedDate);

                ap.Property(p => p.IsDeleted)
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.IsActive)
                    .HasDefaultValue(true)
                    .IsRequired();
            });
        }
    }
}