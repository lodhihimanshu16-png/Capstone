-- 03-create-mcp-user.sql
-- Creates the dedicated MCP database user. Run AFTER 02-mcp-views.sql, as a database administrator.
-- Safe to run more than once: it resets the user to exactly the grants below.
--
-- The password is NOT in this file. ${MCP_DB_PASSWORD} is filled in from the environment when the
-- script is run:   node mcp/scripts/run-sql.mjs db-setup/03-create-mcp-user.sql
-- The host '%' lets the compose and CI containers connect. On a real server, restrict it.
--
-- Grants:
--   SELECT on the five mcp_* views only. No grant on any base table, and none on users.
--   INSERT only on mcp_audit_log (no SELECT, UPDATE or DELETE, so the log cannot be read or edited).
--   No FILE privilege, so SELECT ... INTO OUTFILE is refused by the database as well as by the server.
--
-- Limits set on the account: queries per hour, simultaneous connections.
-- Timeout and row cap: MySQL has no per-user setting for these, so the MCP server sets
-- max_execution_time and sql_select_limit as SESSION variables on every connection it opens.
-- See docs/mcp-security.md for what that does and does not stop.

CREATE USER IF NOT EXISTS 'harbourline_mcp'@'%' IDENTIFIED BY '${MCP_DB_PASSWORD}';

ALTER USER 'harbourline_mcp'@'%' IDENTIFIED BY '${MCP_DB_PASSWORD}'
  WITH MAX_QUERIES_PER_HOUR 5000 MAX_UPDATES_PER_HOUR 5000 MAX_CONNECTIONS_PER_HOUR 600 MAX_USER_CONNECTIONS 3;

REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'harbourline_mcp'@'%';

GRANT SELECT ON harbourline.mcp_claims         TO 'harbourline_mcp'@'%';
GRANT SELECT ON harbourline.mcp_policies       TO 'harbourline_mcp'@'%';
GRANT SELECT ON harbourline.mcp_policyholders  TO 'harbourline_mcp'@'%';
GRANT SELECT ON harbourline.mcp_handlers       TO 'harbourline_mcp'@'%';
GRANT SELECT ON harbourline.mcp_claim_history  TO 'harbourline_mcp'@'%';
GRANT INSERT ON harbourline.mcp_audit_log      TO 'harbourline_mcp'@'%';

FLUSH PRIVILEGES;
