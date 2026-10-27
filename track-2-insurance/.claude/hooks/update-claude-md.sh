#!/usr/bin/env bash
# PostToolUse hook for Bash. After a successful `git commit`, refresh the "Recent changes" block in CLAUDE.md
# (between the two marker lines) with the latest commits, so CLAUDE.md never goes stale about what changed.
# CLAUDE.md is left modified; it goes into the next commit.

input="$(cat)"
cmd="$(printf '%s' "$input" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{try{process.stdout.write(String(JSON.parse(s).tool_input?.command??""))}catch{}})')"

printf '%s' "$cmd" | grep -Eq '(^|[;&|[:space:]])git[[:space:]]+commit([[:space:]]|$)' || exit 0

root="${CLAUDE_PROJECT_DIR:-$(pwd)}"
file="$root/CLAUDE.md"
[ -f "$file" ] || exit 0
grep -q '<!-- recent-commits:start -->' "$file" || exit 0

log="$(git -C "$root" log -5 --pretty='- %h %s' -- . 2>/dev/null)"
[ -n "$log" ] || exit 0

node -e '
const fs = require("node:fs");
const [file, log] = process.argv.slice(1);
const s = fs.readFileSync(file, "utf8");
const re = /<!-- recent-commits:start -->[\s\S]*?<!-- recent-commits:end -->/;
fs.writeFileSync(file, s.replace(re, () => "<!-- recent-commits:start -->\n" + log + "\n<!-- recent-commits:end -->"));
' "$file" "$log"
exit 0
