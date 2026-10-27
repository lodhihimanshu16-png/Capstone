#!/usr/bin/env bash
# Checks .claude/hooks/block-secrets.sh. Usage: bash .claude/hooks/test-block-secrets.sh
hook="$(dirname "$0")/block-secrets.sh"
fail=0

check() { # expected_exit text
  TEXT="$2" node -e 'process.stdout.write(JSON.stringify({tool_input:{content:process.env.TEXT}}))' | bash "$hook" >/dev/null 2>&1
  got=$?
  if [ "$got" -eq "$1" ]; then echo "ok   (exit $got) $2"; else echo "FAIL (exit $got, wanted $1) $2"; fail=1; fi
}

# Must be blocked
check 2 'Server=db;User ID=app;Password=Sup3rSecret;'
check 2 'HARBOURLINE_MCP_DSN=mysql://harbourline_mcp:abc123XYZ@127.0.0.1:3306/harbourline'
check 2 'MYSQL_ROOT_PASSWORD: Root!Pass1'
check 2 'MCP_DB_PASSWORD=hunter2hunter2'
check 2 '-----BEGIN RSA PRIVATE KEY-----'

# Must be allowed
check 0 'Server=db;User ID=app;Password=${DB_PASSWORD};'
check 0 'HARBOURLINE_MCP_DSN=${HARBOURLINE_MCP_DSN}'
check 0 'MYSQL_ROOT_PASSWORD: ${MYSQL_ROOT_PASSWORD}'
check 0 "IDENTIFIED BY '\${MCP_DB_PASSWORD}'"
check 0 'password_hash VARCHAR(100) NOT NULL'
check 0 'export MYSQL_ADMIN_PASSWORD=...        # the admin password'
check 0 'Set the password in an environment variable.'

exit $fail
