namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;
    public class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
    {
        public void Configure(EntityTypeBuilder<Participant> builder)
        {
            builder.ToTable("Participants");

            // Properties
            builder.Property(e => e.OrganizationId)
                .IsRequired();

            builder.Property(e => e.EventId)
                .IsRequired();

            builder.Property(e => e.RaceCategoryId)
                .IsRequired();

            builder.Property(e => e.BibNumber)
                .HasMaxLength(20);

            builder.Property(e => e.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.LastName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Email)
                .HasMaxLength(255);

            builder.Property(e => e.Phone)
                .HasMaxLength(20);

            builder.Property(e => e.Gender)
                .HasMaxLength(10);

            builder.Property(e => e.AgeCategory)
                .HasMaxLength(50);

            builder.Property(e => e.Country)
                .HasMaxLength(100);

            builder.Property(e => e.State)
                .HasMaxLength(100);

            builder.Property(e => e.City)
                .HasMaxLength(100);

            builder.Property(e => e.EmergencyContactName)
                .HasMaxLength(200);

            builder.Property(e => e.EmergencyContactPhone)
                .HasMaxLength(20);

            builder.Property(e => e.MedicalConditions)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.TShirtSize)
                .HasMaxLength(10);

            builder.Property(e => e.RegistrationDate)
                .HasDefaultValueSql("GETUTCDATE()");

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Registered");

            builder.Property(e => e.Notes)
                .HasColumnType("nvarchar(max)");

            // Computed Properties
            builder.Ignore(e => e.FullName);
            builder.Ignore(e => e.Age);

            // Indexes
            builder.HasIndex(e => new { e.EventId, e.BibNumber })
                .IsUnique()
                .HasDatabaseName("IX_Participants_EventId_BibNumber")
                .HasFilter("[BibNumber] IS NOT NULL");

            builder.HasIndex(e => new { e.OrganizationId, e.Email })
                .HasDatabaseName("IX_Participants_OrganizationId_Email");

            builder.HasIndex(e => e.RaceCategoryId)
                .HasDatabaseName("IX_Participants_RaceCategoryId");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Participants)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.Participants)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.RaceCategory)
                .WithMany(rc => rc.Participants)
                .HasForeignKey(e => e.RaceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Result)
                .WithOne(r => r.Participant)
                .HasForeignKey<Results>(r => r.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy);

                ap.Property(p => p.UpdatedDate);

                ap.Property(p => p.IsDeleted)
                    .HasDefaultValue(false);

                ap.Property(p => p.IsActive)
                    .HasDefaultValue(true)
                    .IsRequired();
            });
        }
    }
}