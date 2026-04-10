namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class SupportQueryConfiguration : IEntityTypeConfiguration<SupportQuery>
    {
        public void Configure(EntityTypeBuilder<SupportQuery> builder)
        {
            builder.ToTable("SupportQueries");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.Subject)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Body)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(e => e.SubmitterEmail)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.StatusId)
                .HasDefaultValue(1)
                .IsRequired();

            builder.Property(e => e.QueryTypeId);

            builder.Property(e => e.AssignedToUserId);

            builder.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            builder.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            // Relationships
            builder.HasOne(e => e.Status)
                .WithMany(s => s.SupportQueries)
                .HasForeignKey(e => e.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.QueryType)
                .WithMany(t => t.SupportQueries)
                .HasForeignKey(e => e.QueryTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(e => e.AssignedToUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            builder.HasIndex(e => e.StatusId);
            builder.HasIndex(e => e.AssignedToUserId);
            builder.HasIndex(e => e.SubmitterEmail);
            builder.HasIndex(e => e.CreatedAt);
        }
    }
}
