namespace Runnatics.Data.EF.Config
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using Runnatics.Models.Data.Entities;

    public class SupportQueryStatusConfiguration : IEntityTypeConfiguration<SupportQueryStatus>
    {
        public void Configure(EntityTypeBuilder<SupportQueryStatus> builder)
        {
            builder.ToTable("SupportQueryStatuses");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            builder.Property(e => e.Name)
                .HasMaxLength(50)
                .IsRequired();

            builder.HasIndex(e => e.Name)
                .IsUnique();
        }
    }
}
