#!/usr/bin/env bash
# session-start.sh — Initialize Claude Code session for Runnatics.API
# Called by: hooks.json (on_session_start)
#
# Verifies build, shows branch info, and displays CONTEXT.md status.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
SLN_DIR="$REPO_ROOT/Runnatics/src"
CONTEXT_FILE="$REPO_ROOT/.claude/CONTEXT.md"

echo ""
echo "╔══════════════════════════════════════════════════════╗"
echo "║          Runnatics.API — Session Start               ║"
echo "╚══════════════════════════════════════════════════════╝"
echo ""

# ── Git Info ──
BRANCH=$(git branch --show-current 2>/dev/null || echo "detached")
LAST_COMMIT=$(git log -1 --pretty=format:"%h %s" 2>/dev/null || echo "no commits")
UNCOMMITTED=$(git status --porcelain 2>/dev/null | wc -l | tr -d ' ')

echo "Branch:      $BRANCH"
echo "Last commit: $LAST_COMMIT"
echo "Uncommitted: $UNCOMMITTED file(s)"
echo ""

# ── Branch Convention Check ──
if [[ "$BRANCH" != master ]] && [[ "$BRANCH" != feature/* ]]; then
    echo "WARNING: Branch '$BRANCH' doesn't follow convention: feature/{FeatureName}"
    echo ""
fi

# ── Build Check ──
echo "Verifying build..."
if [ -f "$SLN_DIR/Runnatics.sln" ]; then
    if dotnet build "$SLN_DIR/Runnatics.sln" --no-restore --verbosity quiet 2>/dev/null; then
        echo "Build: OK"
    else
        echo "Build: FAILED — run 'dotnet build' to see errors"
    fi
else
    echo "Build: Solution not found at $SLN_DIR/Runnatics.sln"
fi
echo ""

# ── Context File Status ──
if [ -f "$CONTEXT_FILE" ]; then
    CONTEXT_SIZE=$(wc -l < "$CONTEXT_FILE" | tr -d ' ')
    CONTEXT_MODIFIED=$(git log -1 --format="%ar" -- "$CONTEXT_FILE" 2>/dev/null || echo "never committed")
    echo "CONTEXT.md: $CONTEXT_SIZE lines (last updated: $CONTEXT_MODIFIED)"
    echo ""
    echo "── Recent Context ──"
    tail -20 "$CONTEXT_FILE" 2>/dev/null || echo "(empty)"
else
    echo "CONTEXT.md: Not found — will be created on first task completion."
    mkdir -p "$REPO_ROOT/.claude"
    cat > "$CONTEXT_FILE" << 'EOF'
# Runnatics.API — Shared Context

> This file is the shared memory for all Claude Code agents.
> Every agent MUST read this before starting and write to it after completing a task.

---

## Session Log

_No entries yet._
EOF
    echo "CONTEXT.md: Created fresh."
fi

echo ""
echo "──────────────────────────────────────────────────────"
echo "  Ready. Agents: ef-core | backend | sql"
echo "  Commands: /new-feature | /review"
echo "  Skills: entity-config | api-endpoint"
echo "──────────────────────────────────────────────────────"
echo ""
