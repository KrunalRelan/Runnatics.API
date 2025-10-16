namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
    {
        public virtual void Configure(EntityTypeBuilder<UserSession> builder)
        {
            // Composite key using UserId and TokenHash

            builder.ToTable("UserSessions");

            builder.HasKey(e => e.Id);
            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .IsRequired();
                
            builder.Property(e => e.UserId)
                .IsRequired();

            builder.Property(e => e.TokenHash)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.ExpiresAt)
                .IsRequired();

            builder.Property(e => e.UserAgent)
                .HasMaxLength(1000);

            builder.Property(e => e.IpAddress)
                .HasMaxLength(45);

            // Configure AuditProperties as owned entity
            builder.OwnsOne(e => e.AuditProperties, ap =>
            {
                ap.Property(p => p.IsDeleted)
                    .HasDefaultValue(false)
                    .IsRequired();

                ap.Property(p => p.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()")
                    .IsRequired();

                ap.Property(p => p.CreatedBy)
                    .IsRequired();

                ap.Property(p => p.UpdatedBy);

                ap.Property(p => p.UpdatedDate);

                ap.Property(p => p.IsActive)
                    .HasDefaultValue(true)
                    .IsRequired();
            });

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
