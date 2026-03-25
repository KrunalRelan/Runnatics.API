#!/usr/bin/env bash
# guard-sql.sh — Block commits containing EF Core migration files
# Called by: hooks.json (pre_commit)
#
# This project does NOT use EF Migrations. Schema is managed via hand-written SQL.
# If a migration file is staged, this hook will reject the commit.

set -euo pipefail

echo "[guard-sql] Checking for EF Core migration files..."

# Check for any staged migration files
MIGRATION_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep -iE '(Migrations/|\.Designer\.cs|ModelSnapshot\.cs)' || true)

if [ -n "$MIGRATION_FILES" ]; then
    echo ""
    echo "================================================================"
    echo "  BLOCKED: EF Core migration files detected in staged changes!"
    echo "================================================================"
    echo ""
    echo "  This project does NOT use EF Migrations."
    echo "  Database schema is managed via hand-written SQL scripts."
    echo ""
    echo "  Offending files:"
    echo "$MIGRATION_FILES" | sed 's/^/    - /'
    echo ""
    echo "  To fix:"
    echo "    1. Remove the migration files: git reset HEAD <file>"
    echo "    2. Delete the generated files from disk"
    echo "    3. Write SQL scripts instead (see sql-agent)"
    echo ""
    echo "================================================================"
    exit 1
fi

# Check for migration commands in recent bash history (advisory only)
STAGED_CONTENT=$(git diff --cached -- '*.cs' '*.sh' '*.ps1' 2>/dev/null || true)
if echo "$STAGED_CONTENT" | grep -qiE 'dotnet ef migrations|Add-Migration|Update-Database'; then
    echo ""
    echo "[guard-sql] WARNING: Staged files reference EF migration commands."
    echo "  This project uses hand-written SQL — not EF Migrations."
    echo "  Please verify this is intentional (e.g., documentation)."
    echo ""
fi

echo "[guard-sql] No migration files found. OK."
