using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
    {
        public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
        {
            builder.ToTable("PasswordResetTokens");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("Id")
                .IsRequired();

            builder.Property(x => x.UserId)
                .HasColumnName("UserId")
                .IsRequired();

            builder.Property(x => x.TokenHash)
                .HasColumnName("TokenHash")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(x => x.ExpiresAt)
                .HasColumnName("ExpiresAt")
                .IsRequired();

            builder.Property(x => x.IsUsed)
                .HasColumnName("IsUsed")
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(x => x.IpAddress)
                .HasColumnName("IpAddress")
                .HasMaxLength(45);

            builder.Property(x => x.UserAgent)
                .HasColumnName("UserAgent")
                .HasMaxLength(255);

            // Configure foreign key relationship
            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .HasConstraintName("FK_PasswordResetTokens_Users_UserId")
                .OnDelete(DeleteBehavior.Cascade);

            // Configure owned type for AuditProperties
            builder.OwnsOne(x => x.AuditProperties, auditBuilder =>
            {
                auditBuilder.Property(a => a.CreatedDate)
                    .HasColumnName("CreatedDate")
                    .IsRequired();

                auditBuilder.Property(a => a.CreatedBy)
                    .HasColumnName("CreatedBy")
                    .IsRequired();

                auditBuilder.Property(a => a.UpdatedDate)
                    .HasColumnName("UpdatedDate");

                auditBuilder.Property(a => a.UpdatedBy)
                    .HasColumnName("UpdatedBy");

                auditBuilder.Property(a => a.IsDeleted)
                    .HasColumnName("IsDeleted")
                    .IsRequired()
                    .HasDefaultValue(false);

                auditBuilder.Property(a => a.IsActive)
                    .HasColumnName("IsActive")
                    .IsRequired()
                    .HasDefaultValue(true);
            });

            // Create index for faster lookups
            builder.HasIndex(x => x.UserId)
                .HasDatabaseName("IX_PasswordResetTokens_UserId");

            builder.HasIndex(x => x.ExpiresAt)
                .HasDatabaseName("IX_PasswordResetTokens_ExpiresAt");
        }
    }
}
