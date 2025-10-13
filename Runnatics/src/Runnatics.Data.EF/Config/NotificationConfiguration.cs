namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
    {
        public virtual void Configure(EntityTypeBuilder<Notification> builder)
        {
            // Using AuditProperties.CreatedBy as the primary key since there's no explicit Id
            builder.HasKey(e => new { e.AuditProperties.CreatedBy, e.AuditProperties.CreatedDate, e.Type, e.Recipient });

            builder.Property(e => e.EventId);

            builder.Property(e => e.ParticipantId);

            builder.Property(e => e.Type)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(e => e.Recipient)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Subject)
                .HasMaxLength(255);

            builder.Property(e => e.Message)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending")
                .IsRequired();

            builder.Property(e => e.SentAt);

            builder.Property(e => e.DeliveredAt);

            builder.Property(e => e.ErrorMessage)
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .IsRequired();

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
            builder.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(e => e.Status);
            
            builder.HasIndex(e => e.SentAt);
            
            builder.HasIndex(e => e.EventId);
            
            builder.HasIndex(e => e.Type);
        }
    }
}
