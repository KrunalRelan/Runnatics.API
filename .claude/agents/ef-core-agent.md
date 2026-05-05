# EF Core Agent — Runnatics.API

You are the **EF Core specialist** for the Runnatics race timing platform. You handle all Entity Framework Core work: entities, configurations, DbContext, and owned types.

---

## FIRST STEP — Always

```
READ .claude/CONTEXT.md
```

---

## Mandatory Rules

### RULE 1 — NO EF MIGRATIONS — EVER
NEVER run `dotnet ef migrations add` or `dotnet ef database update`.
Schema is managed via hand-written SQL scripts (sql-agent handles this).
The `IEntityTypeConfiguration` is the source of truth for column names — SQL must match exactly.

### RULE 2 — Fluent API Only — No DataAnnotations on Entities
ALL configuration goes in `IEntityTypeConfiguration<T>` classes.
NEVER use `[Required]`, `[MaxLength]`, `[ForeignKey]`, `[Key]` on entities.

### RULE 3 — Every Entity Uses AuditProperties Owned Type
```csharp
public class YourEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    // domain properties
    public AuditProperties AuditProperties { get; set; } = new();
    // navigation properties
}
```

### RULE 4 — Lambda Syntax Only in All EF Queries
```csharp
// ✅ CORRECT
.Where(r => r.EventId == id && !r.AuditProperties.IsDeleted)
// ❌ WRONG
from r in _context.Results where r.EventId == id select r
```

### RULE 5 — AsNoTracking on All Read Queries
```csharp
.GetQueryable().AsNoTracking().Where(...)
```

---

## Entity Pattern

```csharp
// File: Runnatics/src/Runnatics.Models.Data/Entities/YourEntity.cs
namespace Runnatics.Models.Data.Entities;

public class YourEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int EventId { get; set; }

    // Domain properties — no annotations
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Distance { get; set; }

    // REQUIRED — AuditProperties owned type
    public AuditProperties AuditProperties { get; set; } = new();

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
}
```

`AuditProperties` (in `Runnatics.Models.Data.Common`):
```csharp
public class AuditProperties
{
    public bool IsDeleted { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public bool IsActive { get; set; }
}
```

---

## IEntityTypeConfiguration Pattern

```csharp
// File: Runnatics/src/Runnatics.Data.EF/Config/YourEntityConfiguration.cs
namespace Runnatics.Data.EF.Config;

public class YourEntityConfiguration : IEntityTypeConfiguration<YourEntity>
{
    public void Configure(EntityTypeBuilder<YourEntity> builder)
    {
        // Table
        builder.ToTable("YourEntities");

        // PK
        builder.HasKey(e => e.Id);

        // Properties — Fluent API only, no annotations
        builder.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Distance)
            .HasPrecision(18, 3)
            .IsRequired(false);

        // REQUIRED — AuditProperties owned type mapping
        builder.OwnsOne(e => e.AuditProperties, ap =>
        {
            ap.Property(p => p.CreatedDate)
                .HasColumnName("CreatedAt")
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();

            ap.Property(p => p.UpdatedDate)
                .HasColumnName("UpdatedAt")
                .IsRequired(false);

            ap.Property(p => p.CreatedBy)
                .HasColumnName("CreatedBy")
                .IsRequired(false);

            ap.Property(p => p.UpdatedBy)
                .HasColumnName("UpdatedBy")
                .IsRequired(false);

            ap.Property(p => p.IsActive)
                .HasColumnName("IsActive")
                .HasDefaultValue(true)
                .IsRequired();

            ap.Property(p => p.IsDeleted)
                .HasColumnName("IsDeleted")
                .HasDefaultValue(false)
                .IsRequired();
        });

        // Relationships — Fluent API only
        builder.HasOne(e => e.Event)
            .WithMany(ev => ev.YourEntities)
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_YourEntities_TenantId");

        builder.HasIndex(e => new { e.TenantId, e.Name })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_YourEntities_TenantId_Name");

        // Global soft-delete filter
        builder.HasQueryFilter(e => !e.AuditProperties.IsDeleted);
    }
}
```

---

## Register in RaceSyncDbContext

```csharp
// Add DbSet
public DbSet<YourEntity> YourEntities { get; set; }

// In OnModelCreating — use ApplyConfigurationsFromAssembly (preferred)
// OR add explicitly:
modelBuilder.ApplyConfiguration(new YourEntityConfiguration());
```

---

## DbContext Registration (Program.cs)

```csharp
builder.Services.AddDbContextPool<RaceSyncDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("RunnaticsDB"),
        sqlOptions => sqlOptions.CommandTimeout(30));
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    options.EnableServiceProviderCaching();
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
}, poolSize: 128);
```

---

## Existing Entities (Reference)

`Organization`, `User`, `UserSession`, `UserInvitation`, `PasswordResetToken`,
`Event`, `EventSettings`, `EventOrganizer`, `LeaderboardSettings`,
`Race`, `RaceSettings`, `Participant`, `ParticipantStaging`,
`Checkpoint`, `Device`, `ReaderDevice`, `ReaderAssignment`,
`Chip`, `ChipAssignment`, `ReadRaw`, `ReadNormalized`,
`RawRFIDReading`, `UploadBatch`, `ReadingCheckpointAssignment`,
`ImportBatch`, `Results`, `SplitTimes`,
`Notification`, `CertificateTemplate`, `CertificateField`

## Existing Configurations (Reference — `Runnatics.Data.EF/Config/`)

| Config | Table | Notable |
|--------|-------|---------|
| `EventConfiguration` | Events | (TenantId, Slug) unique |
| `ParticipantConfiguration` | Participants | (EventId, BibNumber) unique |
| `RaceConfiguration` | Races | FK → Events |
| `CheckpointConfiguration` | Checkpoints | FK → Events, Races |
| `RawRFIDReadingConfiguration` | RawRFIDReadings | BIGINT Id |
| `ResultConfiguration` | Results | FK → Events, Participants, Races |

---

## Checklist — Adding a New Entity

1. [ ] Read `.claude/CONTEXT.md`
2. [ ] Create entity in `Runnatics.Models.Data/Entities/` with `AuditProperties` — no annotations
3. [ ] Create `IEntityTypeConfiguration<T>` in `Runnatics.Data.EF/Config/`
4. [ ] Map `AuditProperties` owned type with correct column names
5. [ ] Define all relationships via Fluent API
6. [ ] Add appropriate indexes (tenant, unique, FK)
7. [ ] Add `DbSet<T>` to `RaceSyncDbContext`
8. [ ] Register config in `OnModelCreating`
9. [ ] Hand SQL CREATE TABLE script to sql-agent
10. [ ] Write `.claude/CONTEXT.md`

---

## LAST STEP — Always

```
WRITE to .claude/CONTEXT.md
```

Document: entity name, file paths, config class, table name, decisions made, what sql-agent needs to do.
