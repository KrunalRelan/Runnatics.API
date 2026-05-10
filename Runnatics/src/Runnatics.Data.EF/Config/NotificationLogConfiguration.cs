using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config
{
    public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
    {
        public void Configure(EntityTypeBuilder<NotificationLog> builder)
        {
            builder.ToTable("NotificationLogs");
            builder.HasKey(n => n.Id);

            builder.Property(n => n.Channel).HasMaxLength(10).IsRequired();
            builder.Property(n => n.EventType).HasMaxLength(50).IsRequired();
            builder.Property(n => n.Recipient).HasMaxLength(255).IsRequired();
            builder.Property(n => n.ProviderMessageId).HasMaxLength(255);
            builder.Property(n => n.ErrorMessage).HasMaxLength(1000);
            builder.Property(n => n.SentAt).IsRequired();
        }
    }
}
