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

            // Properties
            builder.Property(e => e.Id)
                .IsRequired();
                
            builder.Property(e => e.Name)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Slug)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Domain)
                .HasMaxLength(255);

            builder.Property(e => e.TimeZone)
                .HasMaxLength(50)
                .HasDefaultValue("Asia/Kolkata");

            builder.Property(e => e.Settings)
                .HasColumnType("nvarchar(max)"); // JSON

            builder.Property(e => e.SubscriptionPlan)
                .HasMaxLength(50);

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            // Indexes
            builder.HasIndex(e => e.Slug)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_Slug");

            builder.HasIndex(e => e.Domain)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_Domain")
                .HasFilter("[Domain] IS NOT NULL");

            // Relationships
            builder.HasMany(e => e.Users)
                .WithOne(u => u.Organization)
                .HasForeignKey(u => u.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(e => e.Events)
                .WithOne(ev => ev.Organization)
                .HasForeignKey(ev => ev.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}