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
            // Properties
            builder.Property(e => e.Id)
                .IsRequired();

            builder.Property(e => e.OrganizationId)
                .IsRequired();

            builder.Property(e => e.Email)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.PasswordHash)
                .HasMaxLength(255);

            builder.Property(e => e.FirstName)
                .HasMaxLength(100);

            builder.Property(e => e.LastName)
                .HasMaxLength(100);

            builder.Property(e => e.Role)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(e => e.LastLoginAt);

            // Indexes
            builder.HasIndex(e => e.OrganizationId)
                .HasDatabaseName("IX_Users_OrganizationId");

            builder.HasIndex(e => new { e.OrganizationId, e.Email })
                .IsUnique()
                .HasDatabaseName("IX_Users_OrganizationId_Email");

            // Relationships
            builder.HasOne(e => e.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(e => e.UserSessions)
                .WithOne(us => us.User)
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}