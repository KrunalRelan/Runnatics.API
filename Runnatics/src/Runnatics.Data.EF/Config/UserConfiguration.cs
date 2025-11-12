namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public virtual void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(e => e.Id);

            // Map to actual database columns (Pascal case)
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.TenantId)
                .HasColumnName("TenantId")
                .IsRequired();

            builder.Property(e => e.Email)
                .HasColumnName("Email")
                .HasMaxLength(510)
                .IsRequired();

            builder.Property(e => e.PasswordHash)
                .HasColumnName("PasswordHash")
                .HasMaxLength(510);

            builder.Property(e => e.FirstName)
                .HasColumnName("FirstName")
                .HasMaxLength(200);

            builder.Property(e => e.LastName)
                .HasColumnName("LastName")
                .HasMaxLength(200);

            builder.Property(e => e.Role)
                .HasColumnName("Role")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.LastLoginAt)
                .HasColumnName("LastLoginAt");

            // Configure AuditProperties to match your database schema
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

            // Foreign Key Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);


            // Indexes
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => new { e.TenantId, e.Email })
                .IsUnique();
        }
    }
}