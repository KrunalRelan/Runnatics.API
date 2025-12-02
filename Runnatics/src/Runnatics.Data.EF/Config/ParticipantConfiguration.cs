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

            // Primary Key
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            // Properties
            builder.Property(e => e.TenantId)
                .HasColumnName("TenantId")
                .IsRequired();

            builder.Property(e => e.EventId)
                .HasColumnName("EventId")
                .IsRequired();

            builder.Property(e => e.RaceId)
                .HasColumnName("RaceId")
                .IsRequired();

            builder.Property(e => e.ImportBatchId)
                .HasColumnName("ImportBatchId");

            builder.Property(e => e.BibNumber)
                .HasColumnName("Bib")
                .HasMaxLength(40);

            builder.Property(e => e.FirstName)
                .HasColumnName("FirstName")
                .HasMaxLength(200);

            builder.Property(e => e.LastName)
                .HasColumnName("LastName")
                .HasMaxLength(200);

            builder.Property(e => e.Email)
                .HasColumnName("Email")
                .HasMaxLength(510);

            builder.Property(e => e.Phone)
                .HasColumnName("Phone")
                .HasMaxLength(100);

            builder.Property(e => e.DateOfBirth)
                .HasColumnName("DateOfBirth")
                .HasColumnType("date");

            builder.Property(e => e.Gender)
                .HasColumnName("Gender")
                .HasMaxLength(20);

            builder.Property(e => e.AgeCategory)
                .HasColumnName("AgeCategory")
                .HasMaxLength(500);

            builder.Property(e => e.Country)
                .HasColumnName("Country")
                .HasMaxLength(200);

            builder.Property(e => e.State)
                .HasColumnName("State")
                .HasMaxLength(200);

            builder.Property(e => e.City)
                .HasColumnName("City")
                .HasMaxLength(200);

            builder.Property(e => e.EmergencyContactName)
                .HasColumnName("EmergencyContact")  // FIXED: was EmergencyContactName
                .HasMaxLength(510);

            builder.Property(e => e.EmergencyContactPhone)
                .HasColumnName("EmergencyPhone")  // FIXED: was EmergencyContactPhone
                .HasMaxLength(100);

            builder.Property(e => e.TShirtSize)
                .HasColumnName("TShirtSize")
                .HasMaxLength(20);

            builder.Property(e => e.RegistrationDate)
                .HasColumnName("StartTime")  // FIXED: was RegistrationDate
                .HasColumnType("datetime2(7)");

            builder.Property(e => e.Status)
                .HasColumnName("RegistrationStatus")  // FIXED: was Status
                .HasMaxLength(40)
                .IsRequired();

            // Ignore properties not in database
            builder.Ignore(e => e.MedicalConditions);  // ADDED: Not in DB
            builder.Ignore(e => e.Notes);              // ADDED: Not in DB

            // Computed Properties
            builder.Ignore(e => e.FullName);
            builder.Ignore(e => e.Age);

            // Indexes
            builder.HasIndex(e => new { e.EventId, e.BibNumber })
                .IsUnique()
                .HasDatabaseName("IX_Participants_EventId_BibNumber")
                .HasFilter("[Bib] IS NOT NULL");  // FIXED: filter column name

            builder.HasIndex(e => new { e.TenantId, e.Email })
                .HasDatabaseName("IX_Participants_TenantId_Email");

            builder.HasIndex(e => e.RaceId)
                .HasDatabaseName("IX_Participants_RaceId");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Participants)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Event)
                .WithMany(ev => ev.Participants)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Race)
                .WithMany(rc => rc.Participants)
                .HasForeignKey(e => e.RaceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(e => e.ImportBatch)
                .WithMany(ib => ib.Participants)
                .HasForeignKey(e => e.ImportBatchId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.Result)
                .WithOne(r => r.Participant)
                .HasForeignKey<Results>(r => r.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy");  // FIXED: Removed MaxLength - it's int in DB

                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasColumnType("datetime2(7)")
                    .IsRequired();

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");  // FIXED: Removed MaxLength - it's int in DB

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt")
                    .HasColumnType("datetime2(7)");

                ap.Property(p => p.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .IsRequired();

                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .IsRequired();
            });
        }
    }
}