# Racetik / Runnatics — Bug Fix Round 2

> **Author:** Kunal Relan
> **Version:** 2.0
> **Date:** June 2026
> **DO NOT modify this file. Place corrections in `.claude/CONTEXT.md`.**

---

## MANDATORY WORKFLOW — 2-1-2 Principle

**You MUST follow this workflow for EVERY bug. No exceptions.**

```
Phase 1a — RESEARCH   → Read all relevant files, understand current behavior
Phase 1b — PLAN       → Write a step-by-step plan, share with user for approval
Phase 2  — EXECUTE    → Implement ONLY what was approved in the plan
Phase 3a — REVIEW     → Self-review every changed file against the plan
Phase 3b — VERIFY     → Run builds, check for regressions, confirm fix
```

### Critical Rules

1. **NEVER start coding before completing Research + Plan and getting user approval**
2. **NEVER modify a file you haven't read first** — read the full file, not just grep
3. **NEVER change code outside the scope of the approved plan**
4. **If you find a related issue during implementation, STOP and report it — do not fix it silently**
5. **After every fix, verify that existing tests still pass and no regressions were introduced**
6. **ONE bug at a time. Complete the full 2-1-2 cycle before moving to the next bug.**

---

## Pre-Task Checklist (Run EVERY time)

```
READ .claude/CONTEXT.md                          → prior decisions, completed work
READ .claude/agents/ef-core-agent.md             → entity conventions
READ .claude/agents/backend-agent.md             → service/controller conventions
READ .claude/agents/sql-agent.md                 → SQL script conventions
READ .claude/agents/support-agent.md             → support ticket domain
```

---

## Architecture Quick Reference

### API Project — `Runnatics.API`

| Layer               | Project                            | Pattern                                       |
|---------------------|------------------------------------|-----------------------------------------------|
| API                 | `Runnatics.Api`                    | Controllers, Program.cs DI                    |
| Application         | `Runnatics.Services` / `.Interface`| Service interfaces + implementations          |
| Client Models       | `Runnatics.Models.Client`          | Request/Response DTOs, `ResponseBase<T>`      |
| Domain              | `Runnatics.Models.Data`            | Entity classes                                |
| Data                | `Runnatics.Data.EF`               | DbContext, `Config/` (IEntityTypeConfiguration)|
| Infrastructure      | `Runnatics.Repositories.Interface` | `IUnitOfWork<C>`, `IGenericRepository<T>`     |
| SQL Scripts         | `SQL/`                             | Idempotent ALTER/CREATE scripts               |

### UI Project — React + Vite + TypeScript + MUI

| Folder              | Purpose                                         |
|---------------------|--------------------------------------------------|
| `src/main/src/pages/`     | Page components                            |
| `src/main/src/components/`| Shared UI components                       |
| `src/main/src/models/`    | TypeScript interfaces/types                |
| `src/main/src/hooks/`     | Custom React hooks                         |
| `src/main/src/contexts/`  | React contexts                             |
| `src/main/src/utility/`   | Helper functions                           |

### Conventions (Non-Negotiable)

- IDs are encrypted via `IdEncryptor` / `IdDecryptor` — decrypt in service, not controller
- Soft delete only: `IsDeleted = true`, `IsActive = false`
- All queries: `!r.AuditProperties.IsDeleted && r.AuditProperties.IsActive`
- Search with 3+ params → POST + request class inheriting `SearchCriteriaBase`
- Pagination → `PagingList<T>` + `SearchResponseBase<T>`
- API responses → wrap in `ResponseBase<T>`
- Lambda syntax everywhere, no LINQ query syntax
- All async methods pass `CancellationToken`
- Read queries use `AsNoTracking()`
- Relationships in `IEntityTypeConfiguration`, no DataAnnotations
- Do not create migrations for sql changes simply share sql query that i need to implement, i will run manually.
- AutoMapper changes only in existing `AutoMapperMappingProfile.cs`
- SQL scripts must be idempotent (IF NOT EXISTS checks)
- `IUnitOfWork<RaceSyncDbContext>` only — never raw DbContext

---

## Sub-Agents

| Agent               | File                                  | Scope                                |
|---------------------|---------------------------------------|--------------------------------------|
| EF Core Agent       | `.claude/agents/ef-core-agent.md`     | Entities, configurations, DbContext  |
| Backend Agent       | `.claude/agents/backend-agent.md`     | Services, controllers, DTOs, mapper  |
| SQL Agent           | `.claude/agents/sql-agent.md`         | SQL scripts, stored procedures       |
| Support Agent       | `.claude/agents/support-agent.md`     | Support ticket workflows             |

---

## Post-Task Checklist

- [ ] `dotnet build` passes with zero errors on API project
- [ ] `npm run build` passes with zero errors on UI project
- [ ] No files were modified outside the approved plan scope
- [ ] `.claude/CONTEXT.md` updated with what was done and any open items
- [ ] Every new SQL script is idempotent and tested
