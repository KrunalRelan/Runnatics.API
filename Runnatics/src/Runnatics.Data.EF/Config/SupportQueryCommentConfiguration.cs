namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class SupportQueryCommentConfiguration : IEntityTypeConfiguration<SupportQueryComment>
    {
        public void Configure(EntityTypeBuilder<SupportQueryComment> builder)
        {
            builder.ToTable("SupportQueryComments");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.SupportQueryId)
                .IsRequired();

            builder.Property(e => e.CommentText)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(e => e.TicketStatusId)
                .IsRequired();

            builder.Property(e => e.NotificationSent)
                .HasDefaultValue(false)
                .IsRequired();

            builder.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            builder.Property(e => e.CreatedByUserId);

            // Relationships
            builder.HasOne(e => e.SupportQuery)
                .WithMany(q => q.Comments)
                .HasForeignKey(e => e.SupportQueryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(e => e.TicketStatus)
                .WithMany(s => s.SupportQueryComments)
                .HasForeignKey(e => e.TicketStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.SupportQueryId);
            builder.HasIndex(e => e.CreatedAt);
        }
    }
}
