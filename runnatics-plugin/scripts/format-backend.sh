#!/usr/bin/env bash
# format-backend.sh — Run dotnet format on staged C# files before commit
# Called by: hooks.json (pre_commit)

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
SLN_DIR="$REPO_ROOT/Runnatics/src"

# Get staged .cs files
STAGED_CS_FILES=$(git diff --cached --name-only --diff-filter=ACM -- '*.cs' || true)

if [ -z "$STAGED_CS_FILES" ]; then
    echo "[format-backend] No staged .cs files — skipping format."
    exit 0
fi

echo "[format-backend] Formatting $(echo "$STAGED_CS_FILES" | wc -l | tr -d ' ') staged .cs file(s)..."

# Run dotnet format on the solution
if [ -f "$SLN_DIR/Runnatics.sln" ]; then
    dotnet format "$SLN_DIR/Runnatics.sln" \
        --include $STAGED_CS_FILES \
        --no-restore \
        --verbosity quiet 2>/dev/null || true
else
    # Fallback: format individual files via project
    for proj in "$SLN_DIR"/Runnatics.*/*.csproj; do
        dotnet format "$proj" \
            --include $STAGED_CS_FILES \
            --no-restore \
            --verbosity quiet 2>/dev/null || true
    done
fi

# Re-stage formatted files
echo "$STAGED_CS_FILES" | while IFS= read -r file; do
    if [ -f "$REPO_ROOT/$file" ]; then
        git add "$REPO_ROOT/$file"
    fi
done

echo "[format-backend] Done."
