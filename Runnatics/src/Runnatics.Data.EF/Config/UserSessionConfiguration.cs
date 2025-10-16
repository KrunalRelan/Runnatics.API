namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
    {
        public virtual void Configure(EntityTypeBuilder<UserSession> builder)
        {
            builder.ToTable("UserSessions");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                .HasColumnName("Id")
                .ValueGeneratedOnAdd()
                .IsRequired();

            builder.Property(e => e.UserId)
                .HasColumnName("UserId")
                .IsRequired();

            builder.Property(e => e.TokenHash)
                .HasColumnName("TokenHash")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.ExpiresAt)
                .HasColumnName("ExpiresAt")
                .IsRequired();

            builder.Property(e => e.UserAgent)
                .HasColumnName("UserAgent")
                .HasMaxLength(1000);

            builder.Property(e => e.IpAddress)
                .HasColumnName("IpAddress")
                .HasMaxLength(45);

            // Relationships
            builder.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(e => e.TokenHash)
                .IsUnique();
            
            builder.HasIndex(e => e.UserId);

            builder.HasIndex(e => e.ExpiresAt);

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
