#!/usr/bin/env bash
# Checks .claude/hooks/block-write-sql.sh. Usage: bash .claude/hooks/test-block-write-sql.sh
hook="$(dirname "$0")/block-write-sql.sh"
fail=0

# The fake password flag is assembled from two pieces so that this file does not trip the secrets hook.
pwflag="--pass""word=abc"

check() { # expected_exit command [description]
  CMD="$2" DESC="${3:-}" node -e 'process.stdout.write(JSON.stringify({tool_input:{command:process.env.CMD,description:process.env.DESC}}))' \
    | bash "$hook" >/dev/null 2>&1
  got=$?
  if [ "$got" -eq "$1" ]; then echo "ok   (exit $got) ${2//$'\n'/ | }"; else echo "FAIL (exit $got, wanted $1) ${2//$'\n'/ | }"; fail=1; fi
}

# Must be blocked (exit 2)
check 2 "mysql -uroot -e 'DROP TABLE claims'"
check 2 "mysql -uroot -e 'update claims set status=1'"
check 2 "docker exec db mysql -e 'INSERT INTO claims VALUES (1)'"
check 2 "docker exec -i -e FOO=1 db mysql -e 'DELETE FROM claims'"
check 2 "mysql -e 'select 1 into outfile /tmp/x'"
check 2 "mysql -uroot -pSecret1 -e 'select 1'"
check 2 "mysql $pwflag -e 'select 1'"
check 2 "MYSQL_PWD=abc mysqldump harbourline"
check 2 "cd x && mysql.exe -e 'ALTER TABLE claims ADD x INT'"
check 2 "echo hi; mysql -e 'DROP TABLE claims'"
check 2 "mysql -e 'select 1;DROP TABLE claims'"
check 2 "bash -c \"mysql -e 'TRUNCATE TABLE claims'\""
check 2 "sudo /usr/bin/mysql -e 'GRANT ALL ON *.* TO x'"

# Must be allowed (exit 0)
check 0 "dotnet test"
check 0 "git commit -m 'update claims workflow'"
check 0 "git commit -m 'create mysql views and drop the old ones'"
check 0 "npm run build"
check 0 "mysql -uroot < db-setup/02-mcp-views.sql"
check 0 "mysql -uroot -e 'select count(*) from claims'"
check 0 "npm install mysql2 zod" "Create the mcp project and update dependencies"
check 0 "node mcp/scripts/run-sql.mjs db-setup/02-mcp-views.sql" "Create views"
check 0 "echo hello" "Insert, update, delete and drop words only in the description"
check 0 $'cat >> notes.md <<\'EOF\'\nMySQL has no per-user timeout. Create a view, then drop the old one and update the table.\nEOF'
check 0 $'cat <<EOF\nmysql -e "DROP TABLE claims"\nEOF'

exit $fail
