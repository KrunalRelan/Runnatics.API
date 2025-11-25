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
      .HasMaxLength(20);

   builder.Property(e => e.FirstName)
      .HasColumnName("FirstName")
 .HasMaxLength(100)
  .IsRequired();

         builder.Property(e => e.LastName)
            .HasColumnName("LastName")
             .HasMaxLength(100)
  .IsRequired();

builder.Property(e => e.Email)
         .HasColumnName("Email")
    .HasMaxLength(255);

      builder.Property(e => e.Phone)
       .HasColumnName("Phone")
                .HasMaxLength(20);

     builder.Property(e => e.DateOfBirth)
     .HasColumnName("DateOfBirth");

        builder.Property(e => e.Gender)
     .HasColumnName("Gender")
       .HasMaxLength(10);

  builder.Property(e => e.AgeCategory)
    .HasColumnName("AgeCategory")
                .HasMaxLength(50);

     builder.Property(e => e.Country)
 .HasColumnName("Country")
        .HasMaxLength(100);

       builder.Property(e => e.State)
         .HasColumnName("State")
        .HasMaxLength(100);

            builder.Property(e => e.City)
       .HasColumnName("City")
      .HasMaxLength(100);

         builder.Property(e => e.EmergencyContactName)
            .HasColumnName("EmergencyContactName")
     .HasMaxLength(200);

  builder.Property(e => e.EmergencyContactPhone)
                .HasColumnName("EmergencyContactPhone")
       .HasMaxLength(20);

        builder.Property(e => e.MedicalConditions)
       .HasColumnName("MedicalConditions")
 .HasColumnType("nvarchar(max)");

       builder.Property(e => e.TShirtSize)
             .HasColumnName("TShirtSize")
        .HasMaxLength(10);

            builder.Property(e => e.RegistrationDate)
   .HasColumnName("RegistrationDate")
       .HasDefaultValueSql("GETUTCDATE()");

     builder.Property(e => e.Status)
         .HasColumnName("Status")
            .HasMaxLength(20)
       .HasDefaultValue("Registered");

            builder.Property(e => e.Notes)
.HasColumnName("Notes")
      .HasColumnType("nvarchar(max)");

        // Computed Properties
 builder.Ignore(e => e.FullName);
            builder.Ignore(e => e.Age);

    // Indexes
     builder.HasIndex(e => new { e.EventId, e.BibNumber })
       .IsUnique()
 .HasDatabaseName("IX_Participants_EventId_BibNumber")
     .HasFilter("[BibNumber] IS NOT NULL");

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
.HasColumnName("CreatedBy")
                 .HasMaxLength(100)
      .IsRequired();

       ap.Property(p => p.CreatedDate)
          .HasColumnName("CreatedAt")
      .HasDefaultValueSql("GETUTCDATE()")
       .IsRequired();

 ap.Property(p => p.UpdatedBy)
           .HasColumnName("UpdatedBy")
        .HasMaxLength(100);

      ap.Property(p => p.UpdatedDate)
      .HasColumnName("UpdatedAt");

    ap.Property(p => p.IsDeleted)
    .HasColumnName("IsDeleted")
 .HasDefaultValue(false);

     ap.Property(p => p.IsActive)
      .HasColumnName("IsActive")
    .HasDefaultValue(true)
       .IsRequired();
     });
  }
    }
}