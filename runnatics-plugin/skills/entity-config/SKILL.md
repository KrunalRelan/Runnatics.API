# Skill: entity-config

Generates a complete `IEntityTypeConfiguration<T>` class for a Runnatics entity, following the exact patterns used in the existing codebase.

---

## Trigger

When user asks to create an EF Core configuration, entity mapping, or table configuration for an entity.

---

## FIRST STEP

```
READ .claude/CONTEXT.md
```

---

## Inputs

| Parameter | Required | Description |
|-----------|----------|-------------|
| `EntityName` | Yes | Name of the entity class (e.g., `Sponsor`) |
| `TableName` | No | SQL table name. Defaults to `{EntityName}s` |
| `HasTenantId` | No | Whether entity is tenant-scoped. Defaults to `true` |
| `UniqueIndexFields` | No | Fields for unique composite index |
| `ForeignKeys` | No | FK relationships (e.g., `EventId -> Events`) |

---

## Template

Generate file at: `Runnatics/src/Runnatics.Data.EF/Config/{EntityName}Configuration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runnatics.Models.Data.Entities;

namespace Runnatics.Data.EF.Config;

public class {EntityName}Configuration : IEntityTypeConfiguration<{EntityName}>
{
    public void Configure(EntityTypeBuilder<{EntityName}> builder)
    {
        builder.ToTable("{TableName}");
        builder.HasKey(e => e.Id);

        // ── Property configurations ──
        // builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        // builder.Property(e => e.Description).HasMaxLength(2000);

        // ── AuditProperties owned type (REQUIRED) ──
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

        // ── Indexes ──
        // builder.HasIndex(e => e.TenantId);
        // builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();

        // ── Foreign Keys ──
        // builder.HasOne<Event>()
        //     .WithMany()
        //     .HasForeignKey(e => e.EventId)
        //     .OnDelete(DeleteBehavior.Restrict);
    }
}
```

---

## Post-Generation Steps

After generating the configuration file:

1. **Register in RaceSyncDbContext** (`Runnatics/src/Runnatics.Data.EF/RaceSyncDbContext.cs`):
   - Add `DbSet<{EntityName}> {EntityName}s { get; set; }` to the class
   - Add `modelBuilder.ApplyConfiguration(new {EntityName}Configuration());` in `OnModelCreating`

2. **Verify entity class exists** in `Runnatics/src/Runnatics.Models.Data/Entities/{EntityName}.cs`
   - Must have `AuditProperties AuditProperties { get; set; } = new();`

---

## Existing Configurations (Reference)

Patterns from real configurations in `Runnatics/src/Runnatics.Data.EF/Config/`:

**EventConfiguration** — Unique slug per tenant:
```csharp
builder.HasIndex(e => new { e.TenantId, e.Slug }).IsUnique();
builder.HasIndex(e => new { e.TenantId, e.Status });
```

**ParticipantConfiguration** — Unique bib per event:
```csharp
builder.HasIndex(e => new { e.EventId, e.BibNumber }).IsUnique();
```

**UserConfiguration** — Unique email per tenant:
```csharp
builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
```

**RawRFIDReadingConfiguration** — Long ID for high volume:
```csharp
builder.HasKey(e => e.Id);  // Id is long, not int
```

---

## LAST STEP

```
WRITE to .claude/CONTEXT.md
```

Document: config class created, table name, indexes, FKs, and DbContext registration status.
