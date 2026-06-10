# Model Selection & Setup Guide

## Recommended Model: Claude Sonnet 4.6

**Why Sonnet 4.6 for this task:**

- These are targeted bug fixes in an existing codebase, not greenfield architecture
- Sonnet 4.6 follows instructions precisely (critical for 2-1-2 compliance)
- Lower cost per token — you have 23 bugs, this will be a long session
- Fast enough for the research-plan-execute-review cycle
- Handles .NET/C# and React/TypeScript equally well

**When to escalate to Opus 4.8:**

- BUG-04/BUG-05 (result engine) — if the timing calculation logic is deeply nested 
  across multiple services and requires understanding complex race topology (loops, 
  shared devices, checkpoint ordering). These were flagged as Opus-candidates in 
  your May 16 session too. Use `claude --model opus` (resolves to Opus 4.8).
- BUG-06 (race category transfer) — if it involves transactional data migration 
  across multiple tables with FK dependencies
- Any bug where Sonnet's plan seems shallow or misses edge cases after 2 attempts

**Do NOT use Opus for:**
- Simple UI fixes (BUG-11, BUG-17, BUG-21)
- Straightforward CRUD fixes (BUG-02, BUG-03, BUG-19)
- CSS/responsive work (BUG-22)

## Claude Code Setup

### Step 1: Update Claude Code
```bash
npm update -g @anthropic-ai/claude-code
```

### Step 2: Set Model
```bash
# Use aliases — simplest approach:
# Sonnet (default for most bugs):
claude --model sonnet

# Opus (for complex result engine / race transfer bugs):
claude --model opus

# Or use explicit model names if you want to pin:
claude --model claude-sonnet-4-6
claude --model claude-opus-4-8
```

> **Note:** On Anthropic API, `opus` alias resolves to Opus 4.8 and `sonnet` resolves to Sonnet 4.6.
> Opus 4.8 is 4x less likely to let its own code flaws pass unremarked — ideal for the REVIEW phase.
> If you want to use Opus 4.8 specifically for the REVIEW+VERIFY phase only, you can switch mid-session.

### Step 3: Launch with the Right Project
```bash
# For API bugs (do these first):
cd C:\Projects\Runnatics.API
claude

# For UI bugs (after API is stable):
cd C:\Projects\Runnatics.UI
claude
```

### Step 4: Safety Settings

Before starting, tell Claude Code:
```
Before we begin: 
- Create a git branch: git checkout -b bugfix/round-2-june2026
- Do NOT push to main
- Commit after each successfully fixed bug with message: "fix(BUG-XX): <description>"
```

## Cross-Model Review Strategy

Just like you wouldn't let a dev review their own PR, don't let the same model review its own code:

```
EXECUTE phase → claude --model sonnet    (Sonnet 4.6 writes the code)
REVIEW phase  → claude --model opus      (Opus 4.8 reviews the code)
```

Opus 4.8 is 4x less likely to let its own code flaws pass unremarked compared to Opus 4.7. Using it specifically for review catches edge cases Sonnet missed, without burning Opus tokens on the initial coding.

## Cost Estimate

Rough estimate for the full 23-bug session:
- Sonnet 4.6: ~$15-25 total (research + execution + review for each bug)
- Opus 4.8 for 3 complex bugs: ~$8-12 additional
- Total: ~$25-35

This is significantly cheaper than Opus for everything (~$80-120).

## Session Strategy

Don't try to fix all 23 bugs in one session. Break it up:

```
Session 1: BUG-01, BUG-02, BUG-03 (P0 blockers) — ~1-2 hours
Session 2: BUG-04, BUG-05, BUG-07 (result engine) — ~2-3 hours, use Opus 4.8 if needed
Session 3: BUG-06, BUG-14 (category transfer + manual time) — ~1-2 hours
Session 4: BUG-08, BUG-10, BUG-13, BUG-20 (leaderboard + public page) — ~2 hours
Session 5: Remaining P2 bugs — ~2-3 hours
```

Between sessions, test the fixes with Deepender/Punit before moving on.
