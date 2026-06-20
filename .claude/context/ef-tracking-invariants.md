# EF Core Tracking Invariants

## Global default: QueryTrackingBehavior.NoTracking (Program.cs)
GetQuery returns FRESH untracked instances with NO identity resolution. So loading the same row twice in one unit of work and attaching both (UpdateRange/Attach/Add) throws: "another instance with the same key value is already being tracked."
- Fix pattern: flush (SaveChanges) between writes, OR load-once-and-reuse, OR bulk ops (EFCore.BulkExtensions bypasses the tracker).
- NEVER add AsNoTracking to a read-then-write method — it makes it worse (forces a second untracked instance, then UpdateRange collides).
- Hit 3x this session: race-move 500, move recalc, manual-time save.

## DbContext concurrency: "second operation started on this context"
Overlapping/un-awaited async DB ops on one request-scoped context. Every DB call must be awaited; save→recalc flows must be sequential, not parallel. Caused by a missing await or parallel (Task.WhenAll / un-sequenced async) DB calls sharing the context.
- A fire-and-forget (Task.Run) that reuses an injected request-scoped service/DbContext collides with the awaited DB calls after it. Run such background work on its OWN DI scope (IServiceScopeFactory.CreateScope → resolve the service there). Also avoids using the request context after it is disposed at end-of-request.
