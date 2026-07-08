# EF Core Tracking Invariants

## Global default: QueryTrackingBehavior.NoTracking (Program.cs)
GetQuery returns FRESH untracked instances with NO identity resolution. So loading the same row twice in one unit of work and attaching both (UpdateRange/Attach/Add) throws: "another instance with the same key value is already being tracked."
- Fix pattern: flush (SaveChanges) between writes, OR load-once-and-reuse, OR bulk ops (EFCore.BulkExtensions bypasses the tracker).
- NEVER add AsNoTracking to a read-then-write method — it makes it worse (forces a second untracked instance, then UpdateRange collides).
- Hit 4x: race-move 500, move recalc, manual-time save, and (2026-07-07) the save-path funnel — see below.
- SaveChanges does NOT detach: entities stay tracked (Unchanged) after commit, so the collision reaches across "completed" transactions within one request.

## Funnel-from-a-request rule (2026-07-07, toggle 500 — SplitTimes double-track)
A service method that WRITES tracked entities and then invokes the reprocess funnel
(ProcessCompleteWorkflowAsync) must run the funnel on a FRESH DI SCOPE
(IServiceScopeFactory.CreateScope → resolve IRFIDImportService there): the request context still
tracks the saved instances, and the funnel re-loads those rows as fresh instances → attach throws.
The throw can surface LATE and misleadingly — Phase 2.4's catch swallowed its ReadNormalized
collision into a warning (silently no-opping the override apply), so the first VISIBLE error was
Phase 2.45's SplitTimes attach. IUserContextService reads through IHttpContextAccessor
(AsyncLocal), so a scope created during the request keeps the caller's identity for audit fields.
Applied to: RecordManualTimeAsync + RemoveManualTimeAsync. ProcessParticipantResultAsync
deliberately does NOT (documented: nothing tracked before its funnel call).

## Pipeline-phase rewrite rule (same incident)
A pipeline phase that REWRITES rows another phase in the same run may have loaded/updated
(Phase 2.45 rewriting what Phase 2.4 just attached) must use tracker-bypass bulk ops
(BulkUpdateAsync), never UpdateRange/Attach — and NOT inside ExecuteInTransactionAsync
(bulk ops don't compose with deferred change-tracked writes in one strategy transaction;
see the Phase 2.4 comment).

## DbContext concurrency: "second operation started on this context"
Overlapping/un-awaited async DB ops on one request-scoped context. Every DB call must be awaited; save→recalc flows must be sequential, not parallel. Caused by a missing await or parallel (Task.WhenAll / un-sequenced async) DB calls sharing the context.
- A fire-and-forget (Task.Run) that reuses an injected request-scoped service/DbContext collides with the awaited DB calls after it. Run such background work on its OWN DI scope (IServiceScopeFactory.CreateScope → resolve the service there). Also avoids using the request context after it is disposed at end-of-request.
