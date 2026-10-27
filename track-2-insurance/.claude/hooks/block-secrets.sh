#!/usr/bin/env bash
# PreToolUse hook for Edit and Write. Blocks (exit 2) text that is about to be written to a file if it contains
# a password, a connection string with a password in it, or a private key.
# ${ENV_VAR} placeholders and empty passwords are fine.

input="$(cat)"
text="$(printf '%s' "$input" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{try{const t=JSON.parse(s).tool_input??{};process.stdout.write(String(t.content??"")+"\n"+String(t.new_string??""))}catch{}})')"

block() { echo "Blocked: this text looks like it contains a secret ($1). Use an environment variable or a placeholder such as \${NAME}, and do not commit the real value." >&2; exit 2; }

printf '%s' "$text" | grep -Eq -- '-----BEGIN [A-Z ]*PRIVATE KEY-----' && block "private key"
printf '%s' "$text" | grep -Eiq '(^|[^A-Za-z_])Password=[^;"[:space:]$]{3,}' && block "Password= in a connection string"
printf '%s' "$text" | grep -Eiq '[a-z]+://[^:@/[:space:]]+:[^@/[:space:]$%{<][^@/[:space:]]*@' && block "user:password@ in a URL"
printf '%s' "$text" | grep -Eiq '(MYSQL_ROOT_PASSWORD|MYSQL_PASSWORD|MCP_DB_PASSWORD)[=:][[:space:]]*["'\'']?[A-Za-z0-9!#$%^&*_-]{4,}' \
  && ! printf '%s' "$text" | grep -Eiq '(MYSQL_ROOT_PASSWORD|MYSQL_PASSWORD|MCP_DB_PASSWORD)[=:][[:space:]]*["'\'']?(\$\{|\$[A-Z])' \
  && block "database password variable with a value"
exit 0
