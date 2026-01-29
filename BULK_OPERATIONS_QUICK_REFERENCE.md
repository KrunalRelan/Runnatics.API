# Quick Reference: Bulk Operations Usage

## How to Use Bulk Operations in Your Code

### 1. Bulk Insert
```csharp
var entities = new List<MyEntity>
{
    new MyEntity { Name = "Item 1" },
    new MyEntity { Name = "Item 2" },
    // ... 1000+ more items
};

var repo = _repository.GetRepository<MyEntity>();
await repo.BulkInsertAsync(entities); // Single SQL BULK INSERT
// No need to call SaveChangesAsync()
```

### 2. Bulk Update
```csharp
var entities = await repo.GetQuery(e => e.Status == "Pending").ToListAsync();

foreach (var entity in entities)
{
    entity.Status = "Processed";
    entity.UpdatedDate = DateTime.UtcNow;
}

await repo.BulkUpdateAsync(entities); // Single SQL MERGE statement
// No need to call SaveChangesAsync()
```

### 3. Bulk Delete
```csharp
var entitiesToDelete = await repo.GetQuery(e => e.CreatedDate < cutoffDate).ToListAsync();

await repo.BulkDeleteAsync(entitiesToDelete); // Single SQL DELETE
// No need to call SaveChangesAsync()
```

### 4. Bulk Insert or Update (Upsert)
```csharp
var entities = new List<MyEntity>
{
    new MyEntity { Id = 1, Name = "Updated" }, // Exists - will update
    new MyEntity { Id = 0, Name = "New" },     // Doesn't exist - will insert
};

await repo.BulkInsertOrUpdateAsync(entities); // Single SQL MERGE
// No need to call SaveChangesAsync()
```

## Configuration Options

### Basic Configuration
```csharp
var config = new BulkConfig
{
    BatchSize = 5000,          // Process 5000 records at a time
    BulkCopyTimeout = 300,     // 5 minutes timeout
    EnableStreaming = true,    // For very large datasets
    SetOutputIdentity = true   // Retrieve auto-generated IDs
};

await repo.BulkInsertAsync(entities, config);
```

### Advanced Configuration
```csharp
var config = new BulkConfig
{
    // Performance
    BatchSize = 10000,
    BulkCopyTimeout = 600,
    EnableStreaming = true,
    
    // Identity handling
    SetOutputIdentity = false,      // Don't retrieve IDs (faster)
    PreserveInsertOrder = false,   // Don't maintain order (faster)
    
    // Change tracking
    TrackingEntities = false,       // Don't track entities (faster)
    
    // Column handling
    PropertiesToInclude = new List<string> { "Name", "Status" },
    PropertiesToExclude = new List<string> { "InternalNotes" },
    
    // Transaction
    UseTempDB = true               // Use TempDB for staging
};
```

## When to Use Each Operation

### Use `BulkInsertAsync` when:
- Inserting 100+ new records
- Importing data from files
- Creating initial seed data
- Batch creating records

### Use `BulkUpdateAsync` when:
- Updating 100+ existing records
- Batch status updates
- Recalculating computed fields
- Mass data corrections

### Use `BulkDeleteAsync` when:
- Deleting 100+ records
- Data cleanup operations
- Archiving old data
- Batch removal operations

### Use `BulkInsertOrUpdateAsync` when:
- Synchronizing data from external sources
- Implementing upsert logic
- Merging datasets
- Handling both creates and updates

## Performance Guidelines

| Records | Use Standard EF | Use Bulk Operations |
|---------|----------------|---------------------|
| 1-50 | ✅ Yes | ❌ Not needed |
| 50-100 | ⚠️ Either works | ✅ Recommended |
| 100+ | ❌ Too slow | ✅ Strongly recommended |
| 1000+ | ❌ Very slow | ✅ Required |
| 10000+ | ❌ Extremely slow | ✅ Required + Configure |

## Common Patterns

### Pattern 1: Process in Batches
```csharp
const int batchSize = 5000;
for (int i = 0; i < allEntities.Count; i += batchSize)
{
    var batch = allEntities.Skip(i).Take(batchSize).ToList();
    await repo.BulkInsertAsync(batch);
    _logger.LogInformation("Processed {Count} of {Total}", i + batch.Count, allEntities.Count);
}
```

### Pattern 2: Conditional Update
```csharp
var entitiesToUpdate = entities
    .Where(e => e.NeedsUpdate)
    .ToList();

if (entitiesToUpdate.Count > 0)
{
    await repo.BulkUpdateAsync(entitiesToUpdate);
}
```

### Pattern 3: Transaction Wrapping
```csharp
await _repository.BeginTransactionAsync();
try
{
    await repo1.BulkInsertAsync(entities1);
    await repo2.BulkUpdateAsync(entities2);
    await _repository.CommitTransactionAsync();
}
catch
{
    await _repository.RollbackTransactionAsync();
    throw;
}
```

## LINQ Replacements for For Loops

### Example 1: Transform with Index
```csharp
// ❌ BEFORE: For loop
var results = new List<Result>();
var rank = 1;
foreach (var item in items)
{
    results.Add(new Result { Item = item, Rank = rank++ });
}

// ✅ AFTER: LINQ
var results = items.Select((item, index) => 
    new Result { Item = item, Rank = index + 1 }
).ToList();
```

### Example 2: Flatten Nested Loops
```csharp
// ❌ BEFORE: Nested for loops
var allResults = new List<Result>();
foreach (var group in groups)
{
    var rank = 1;
    foreach (var item in group.Items.OrderBy(x => x.Score))
    {
        item.Rank = rank++;
        allResults.Add(item);
    }
}

// ✅ AFTER: LINQ SelectMany
var allResults = groups
    .SelectMany(group => group.Items
        .OrderBy(x => x.Score)
        .Select((item, index) =>
        {
            item.Rank = index + 1;
            return item;
        }))
    .ToList();
```

### Example 3: Filter and Transform
```csharp
// ❌ BEFORE: For loop with condition
var validItems = new List<Item>();
foreach (var item in items)
{
    if (item.IsValid)
    {
        item.ProcessedDate = DateTime.UtcNow;
        validItems.Add(item);
    }
}

// ✅ AFTER: LINQ Where + Select
var validItems = items
    .Where(item => item.IsValid)
    .Select(item =>
    {
        item.ProcessedDate = DateTime.UtcNow;
        return item;
    })
    .ToList();
```

## Best Practices

1. **Always use bulk operations for 100+ records**
2. **Disable change tracking when not needed** (`TrackingEntities = false`)
3. **Use HashSet for fast lookups** instead of Dictionary when you only need to check existence
4. **Prefer LINQ over for loops** for better readability and performance
5. **Configure batch size** based on your data size and available memory
6. **Use transactions** for related operations to ensure data consistency
7. **Log progress** for long-running operations
8. **Monitor memory usage** for very large datasets

## Troubleshooting

### Issue: Out of Memory
**Solution**: Use smaller batch size and `EnableStreaming = true`

### Issue: Timeout Errors
**Solution**: Increase `BulkCopyTimeout` or reduce `BatchSize`

### Issue: Identity Values Not Set
**Solution**: Set `SetOutputIdentity = true` in config

### Issue: Triggers Not Firing
**Solution**: Bulk operations bypass triggers - handle in application code

### Issue: Navigation Properties Not Loaded
**Solution**: Bulk operations don't load navigation properties - query separately if needed
