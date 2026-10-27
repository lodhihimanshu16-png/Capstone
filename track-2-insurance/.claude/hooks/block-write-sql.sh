#!/usr/bin/env bash
# PreToolUse hook for the Bash tool. Blocks (exit code 2) write or DDL SQL sent to a MySQL client, and
# passwords typed on the command line. The logic is in block-write-sql.mjs.
exec node "$(dirname "$0")/block-write-sql.mjs"
