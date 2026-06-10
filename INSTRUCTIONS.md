# Claude Code Instructions — Racetik Bug Fix Round 2

> **How to use this file:**
> 1. Place all `.md` files from this package into your repo (instructions below)
> 2. Open Claude Code in your API project directory
> 3. Copy-paste the PROMPT below into Claude Code
> 4. Wait for the Research + Plan output
> 5. Review the plan, approve or correct it
> 6. Then paste the EXECUTE prompt
> 7. After execution, paste the REVIEW prompt

---

## FILE PLACEMENT

### API Project (`Runnatics.API/`)

```
Runnatics.API/
├── CLAUDE.md                          ← REPLACE with the new CLAUDE.md from this package
├── .claude/
│   ├── CONTEXT.md                     ← Keep existing, or create fresh if corrupted
│   └── agents/
│       ├── ef-core-agent.md           ← Keep existing
│       ├── backend-agent.md           ← Keep existing
│       ├── sql-agent.md               ← Keep existing
│       └── support-agent.md           ← Keep existing
├── BUG-PLAN.md                        ← NEW — place at repo root
└── ... (existing project files)
```

### UI Project (separate repo)

```
Runnatics.UI/
├── CLAUDE.md                          ← Keep existing or create one
├── BUG-PLAN.md                        ← Copy the same BUG-PLAN.md here
└── ... (existing project files)
```

---

## PROMPT 1 — RESEARCH (Paste into Claude Code for API project)

```
You are starting Bug Fix Round 2 for Racetik.

READ these files first, in this exact order:
1. CLAUDE.md
2. .claude/CONTEXT.md (if it exists)
3. .claude/agents/ef-core-agent.md
4. .claude/agents/backend-agent.md
5. .claude/agents/sql-agent.md
6. BUG-PLAN.md

You are in RESEARCH PHASE. Do NOT write any code yet.

Start with BUG-01 (EPC Mapping — Multiple Tags Auto-Map).

RESEARCH TASKS for BUG-01:
1. Find every file related to EPC mapping / BIB mapping:
   - The controller endpoint(s)
   - The service method(s)
   - The entity/model classes (EpcMapping, BibMapping, or similar)
   - The SignalR hub (if EPC mapping uses real-time scanning)
   - The entity configuration in Runnatics.Data.EF/Config/

2. Read each file completely. Do NOT skim.

3. Answer these questions:
   a. What is the exact flow when an EPC is scanned?
   b. How does the system currently decide which BIB to map an EPC to?
   c. Is there ANY existing logic for handling multiple EPCs in one scan?
   d. Where is the "auto-advance to next BIB" logic?
   e. What happens if the same EPC is scanned twice?
   f. Is there a dedup window or batch grouping?

4. Write a DETAILED PLAN with:
   - Exact file paths you will modify
   - Exact method names you will change
   - What the current code does (paste relevant snippets)
   - What you will change it to (describe precisely)
   - What you will NOT change

5. After BUG-01 research, move to BUG-02 (Participant Range Sequencing):
   - Find the bulk/range participant creation endpoint
   - Find how BIB numbers are assigned
   - Write detailed plan

6. Then BUG-03 (Checkpoint Creation Error):
   - Find checkpoint creation endpoint and service
   - Find what validation/constraint is failing
   - Check the error handling — is the real DB error being swallowed?
   - Write detailed plan

PRESENT your research findings and plans for BUG-01, BUG-02, BUG-03 together.
I will review and approve before you write any code.

CRITICAL: Do NOT modify any files. This is research only.
```

---

## PROMPT 2 — EXECUTE (Paste AFTER you've approved the plan)

```
I have reviewed and approved your plan for BUG-01, BUG-02, BUG-03.

You are now in EXECUTE PHASE. Follow CLAUDE.md 2-1-2 rules strictly.

EXECUTION RULES:
1. Implement ONE bug at a time, starting with BUG-01
2. After each bug fix, run: dotnet build
3. If build fails, fix the build error before moving to the next bug
4. Do NOT touch any file not listed in the approved plan
5. If you discover a related issue, STOP and tell me — do not fix it silently
6. After all 3 bugs are fixed, list EVERY file you changed with a one-line summary

Start with BUG-01 now.
```

---

## PROMPT 3 — REVIEW + VERIFY (Paste AFTER execution completes)

```
You are now in REVIEW + VERIFY PHASE.

REVIEW:
1. Re-read every file you modified
2. For each change, verify:
   a. Does it match the approved plan?
   b. Does it follow the conventions in CLAUDE.md?
   c. Could it break any OTHER functionality? (Check callers of modified methods)
   d. Are there null checks, error handling, edge cases?
   e. Is the code style consistent with the surrounding code?

3. Check for regressions:
   a. Search for other places that call the methods you modified
   b. Verify those callers still work with your changes
   c. Check if any DTO shapes changed that the UI depends on

4. Run: dotnet build
5. Report: PASS or FAIL with details

VERIFY:
1. For BUG-01: Simulate the scenario — if 3 EPCs arrive in one scan batch, 
   does the code correctly reject them? Trace the code path.
2. For BUG-02: If range 1-50 is requested, trace the code to confirm 
   BIBs 1,2,3...50 are created in order.
3. For BUG-03: What was the actual error? Is it now handled with a 
   descriptive message?

Write a VERIFICATION REPORT with:
- Each bug: FIXED / PARTIALLY FIXED / NOT FIXED
- Any side effects or risks identified
- Recommendations for testing

Update .claude/CONTEXT.md with:
- What was fixed
- What files were changed
- Any open items or risks
```

---

## PROMPT 4 — UI BUGS (Paste into Claude Code for UI project, AFTER API bugs are fixed)

```
You are starting the UI portion of Bug Fix Round 2 for Racetik.

READ these files first:
1. CLAUDE.md (if exists)
2. BUG-PLAN.md

You are in RESEARCH PHASE. Do NOT write any code yet.

The API bugs BUG-01, BUG-02, BUG-03 have been fixed. The API now:
- BUG-01: Returns an error response when multiple EPCs are detected in one scan
- BUG-02: Returns participants with sequential BIB numbers
- BUG-03: Returns descriptive error messages on checkpoint creation failure

RESEARCH TASKS:
1. Find the BIB Mapping page component:
   - How does it receive EPC scan results?
   - Where is the "Mapped" / "Not mapped" badge rendered?
   - Does it currently handle a "Multiple EPC" error state?

2. Find the Checkpoint creation dialog:
   - How does it display errors from the API?
   - Is it catching the full error response or just showing a generic message?

3. Find the Participant list page:
   - How are participants sorted after range creation?
   - Is sorting by BIB number ascending?

4. Find the results page:
   - Where are split/cumulative timings calculated or displayed?
   - Where is the DNF/Finished status rendered?

5. Find the leaderboard components:
   - Overall Result tab
   - Age Category tabs
   - Gun Time vs Chip Time logic

PRESENT your findings. I will review before you code.
```

---

## PROMPT 5 — SUBSEQUENT ROUNDS (Use for BUG-04 through BUG-23)

```
Continue Bug Fix Round 2. We are now on [BUG-XX].

READ BUG-PLAN.md for the bug description.

You are in RESEARCH PHASE for [BUG-XX].

1. Find all relevant files (list them)
2. Read each one completely
3. Answer the research questions from BUG-PLAN.md
4. Write a detailed plan with exact file paths, method names, and changes

PRESENT the plan. Do NOT write code until I approve.
```

---

## ROLLBACK INSTRUCTION

If Claude Code makes a mess, use this:

```
STOP. Do not make any more changes.

List EVERY file you have modified in this session with the exact changes made.

I need to review before continuing. If any changes are incorrect, 
I will tell you to revert specific files using: git checkout -- <filepath>
```
