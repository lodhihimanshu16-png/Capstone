#!/usr/bin/env bash
# PostToolUse hook for Edit and Write. After a code edit, build and run the fast tests for that part of the repo.
# Exit code 2 sends the failure back to Claude so it can fix it. Other edits (docs, SQL, config) do nothing.

input="$(cat)"
file="$(printf '%s' "$input" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{try{const j=JSON.parse(s);process.stdout.write(String(j.tool_input?.file_path??""))}catch{}})')"
file="${file//\\//}"
root="${CLAUDE_PROJECT_DIR:-$(pwd)}"

fail() { echo "$1" >&2; exit 2; }

# While several agents edit at once, a build after every edit would collide and report half-finished work.
# Create .claude/.hooks-paused to switch this hook off, and delete the file to switch it back on.
[ -f "$root/.claude/.hooks-paused" ] && exit 0

case "$file" in
  */backend/*.cs)
    cd "$root/backend" || exit 0
    out="$(dotnet build Harbourline.sln --nologo -v q 2>&1)" || fail "dotnet build failed after editing $file:
$(printf '%s' "$out" | tail -30)"
    out="$(dotnet test Harbourline.sln --no-build --nologo -v q 2>&1)" || fail "dotnet test failed after editing $file:
$(printf '%s' "$out" | tail -40)"
    ;;
  */mcp/src/*.ts|*/mcp/tests/*.ts)
    cd "$root/mcp" || exit 0
    out="$(npx tsc -p tsconfig.json --noEmit 2>&1)" || fail "TypeScript failed after editing $file:
$(printf '%s' "$out" | tail -30)"
    out="$(npx vitest run tests/guard.test.ts 2>&1)" || fail "Guard tests failed after editing $file:
$(printf '%s' "$out" | tail -40)"
    ;;
  */frontend/src/*.ts|*/frontend/src/*.tsx)
    cd "$root/frontend" || exit 0
    [ -d node_modules ] || exit 0
    out="$(npx tsc -b 2>&1)" || fail "Frontend type-check failed after editing $file:
$(printf '%s' "$out" | tail -30)"
    ;;
esac
exit 0
