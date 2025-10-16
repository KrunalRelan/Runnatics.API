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
                .HasColumnName("Name")
                .HasMaxLength(510)
                .IsRequired();

            builder.Property(e => e.Slug)
                .HasColumnName("Slug")
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(e => e.Domain)
                .HasColumnName("Domain")
                .HasMaxLength(510);

            builder.Property(e => e.TimeZone)
                .HasColumnName("TimeZone")
                .HasMaxLength(100)
                .HasDefaultValue("Asia/Kolkata");

            builder.Property(e => e.Settings)
                .HasColumnName("Settings")
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.SubscriptionPlan)
                .HasColumnName("SubscriptionPlan")
                .HasMaxLength(100);

            builder.Property(e => e.Status)
                .HasColumnName("Status")
                .HasMaxLength(40)
                .HasDefaultValue("Active");

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
                    .HasColumnName("CreatedBy");

                ap.Property(p => p.UpdatedBy)
                    .HasColumnName("UpdatedBy");

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
            builder.Ignore(e => e.Website);
            builder.Ignore(e => e.LogoUrl);
            builder.Ignore(e => e.Description);
            builder.Ignore(e => e.SubscriptionStartDate);
            builder.Ignore(e => e.SubscriptionEndDate);
            builder.Ignore(e => e.IsSubscriptionActive);
            builder.Ignore(e => e.Currency);
            builder.Ignore(e => e.Country);
            builder.Ignore(e => e.City);
            builder.Ignore(e => e.IsVerified);
            builder.Ignore(e => e.MaxEvents);
            builder.Ignore(e => e.MaxParticipantsPerEvent);
            builder.Ignore(e => e.MaxUsers);
            
            // Ignore computed properties
            builder.Ignore(e => e.TotalUsers);
            builder.Ignore(e => e.ActiveEvents);
            builder.Ignore(e => e.PendingInvitations);
            builder.Ignore(e => e.AccessUrl);
            builder.Ignore(e => e.IsSubscriptionExpired);
            builder.Ignore(e => e.DaysUntilSubscriptionExpiry);

            // Indexes
            builder.HasIndex(e => e.Slug)
                .IsUnique();

            builder.HasIndex(e => e.Domain)
                .IsUnique();
        }
    }
}