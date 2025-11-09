namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class EventSettingsConfiguration : IEntityTypeConfiguration<EventSettings>
    {
        public void Configure(EntityTypeBuilder<EventSettings> builder)
        {
            builder.ToTable("EventSettings");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                    .HasColumnName("Id")
                    .ValueGeneratedOnAdd()
                    .IsRequired();

            // Properties
            builder.Property(e => e.EventId)
                     .HasColumnName("EventId")
                     .IsRequired();

            builder.Property(e => e.RemoveBanner)
                    .HasColumnName("RemoveBanner")
                    .HasDefaultValue(false)
                    .IsRequired();

            builder.Property(e => e.Published)
                   .HasColumnName("Published")
                   .HasDefaultValue(false)
                   .IsRequired();

            builder.Property(e => e.RankOnNet)
                 .HasColumnName("RankOnNet")
                 .HasDefaultValue(true)
                 .IsRequired();

            builder.Property(e => e.ShowResultSummaryForRaces)
                 .HasColumnName("ShowResultSummaryForRaces")
                 .HasDefaultValue(true)
                 .IsRequired();

            builder.Property(e => e.UseOldData)
                 .HasColumnName("UseOldData")
                 .HasDefaultValue(false)
                 .IsRequired();

            builder.Property(e => e.ConfirmedEvent)
                 .HasColumnName("ConfirmedEvent")
                 .HasDefaultValue(false)
                 .IsRequired();

            builder.Property(e => e.AllowNameCheck)
                 .HasColumnName("AllowNameCheck")
                 .HasDefaultValue(true)
                 .IsRequired();

            builder.Property(e => e.AllowParticipantEdit)
                 .HasColumnName("AllowParticipantEdit")
                 .HasDefaultValue(true)
                 .IsRequired();

            // Indexes
            builder.HasIndex(e => e.EventId)
                .IsUnique()
                .HasDatabaseName("IX_EventSettings_EventId");

            // Relationships
            builder.HasOne(e => e.Event)
                .WithOne(ev => ev.EventSettings)
                .HasForeignKey<EventSettings>(e => e.EventId)
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
