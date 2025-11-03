namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
    {
        public virtual void Configure(EntityTypeBuilder<Organization> builder)
        {
            builder.ToTable("Organizations");

            builder.HasKey(e => e.Id);

            // Map to actual database columns (Pascal case as per your schema)
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();          
                
            builder.Property(e => e.Name)
                .HasColumnName("OrganizationName")
                .HasMaxLength(510)
                .IsRequired();

            builder.Property(e => e.Domain)
                .HasColumnName("Domain")
                .HasMaxLength(510);

            // Configure AuditProperties as owned entity to match your database schema
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.CreatedDate)
                    .HasColumnName("CreatedAt")
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.UpdatedDate)
                    .HasColumnName("UpdatedAt");

                ap.Property(p => p.CreatedBy)
                    .HasColumnName("CreatedBy")
                    .HasMaxLength(100);

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy")
                    .HasMaxLength(100);

                // Ignore IsActive from AuditProperties since Organization has its own IsActive property
                ap.Property(p => p.IsActive)
                    .HasColumnName("IsActive")
                    .HasDefaultValue(true)
                    .IsRequired();

                ap.Property(p => p.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .HasDefaultValue(false)
                    .IsRequired();
            });

            // Ignore properties that don't exist in the database
            builder.Ignore(e => e.Email);
            builder.Ignore(e => e.PhoneNumber);
            builder.Ignore(e => e.MaxEvents);
            builder.Ignore(e => e.MaxParticipantsPerEvent);
            builder.Ignore(e => e.MaxUsers);
            
            // Ignore computed properties
            builder.Ignore(e => e.TotalUsers);
            builder.Ignore(e => e.ActiveEvents);
            builder.Ignore(e => e.PendingInvitations);
            builder.HasIndex(e => e.Domain)
                .IsUnique();
        }
    }
}