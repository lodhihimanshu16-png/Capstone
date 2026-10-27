import mysql, { type Connection } from 'mysql2/promise';

// Limits enforced in the server. The same values are also set on every database session, so the
// database enforces them too.
export const MAX_ROWS = Number(process.env.MCP_MAX_ROWS ?? 500);
export const MAX_EXECUTION_MS = Number(process.env.MCP_MAX_EXECUTION_MS ?? 5000);

export interface QueryResult {
  rows: Record<string, unknown>[];
  truncated: boolean;
}

function connectionOptions() {
  const dsn = process.env.HARBOURLINE_MCP_DSN;
  if (!dsn) throw new Error('HARBOURLINE_MCP_DSN is not set. It must hold the MCP database connection string.');
  const url = new URL(dsn);
  return {
    host: url.hostname,
    port: Number(url.port || 3306),
    user: decodeURIComponent(url.username),
    password: decodeURIComponent(url.password),
    database: url.pathname.replace(/^\//, ''),
    multipleStatements: false, // the driver itself refuses a second statement
    connectTimeout: 5000,
  };
}

async function open(readOnly: boolean): Promise<Connection> {
  const conn = await mysql.createConnection(connectionOptions());
  if (readOnly) {
    // Database-side timeout and row cap, plus a read-only session.
    await conn.query(`SET SESSION max_execution_time = ${MAX_EXECUTION_MS}, sql_select_limit = ${MAX_ROWS + 1}`);
    await conn.query('SET SESSION TRANSACTION READ ONLY');
  }
  return conn;
}

/** Run one already-checked SELECT. Rows past MAX_ROWS are dropped and `truncated` is set. */
export async function runSelect(sql: string, values: unknown[] = []): Promise<QueryResult> {
  const conn = await open(true);
  try {
    const [rows] = await conn.query({ sql, values, timeout: MAX_EXECUTION_MS + 1000 });
    const all = rows as Record<string, unknown>[];
    return { rows: all.slice(0, MAX_ROWS), truncated: all.length > MAX_ROWS };
  } finally {
    await conn.end().catch(() => undefined);
  }
}

export interface AuditEntry {
  tool: string;
  args: unknown;
  outcome: 'ok' | 'refused' | 'error';
  reason?: string;
  rowCount?: number;
  durationMs: number;
}

/** Record one call in mcp_audit_log. The MCP user can only INSERT into this table. */
export async function writeAudit(entry: AuditEntry): Promise<void> {
  const conn = await open(false);
  try {
    await conn.query(
      'INSERT INTO mcp_audit_log (tool_name, arguments, outcome, reason, row_count, duration_ms) VALUES (?, ?, ?, ?, ?, ?)',
      [
        entry.tool,
        JSON.stringify(entry.args ?? null).slice(0, 4000),
        entry.outcome,
        entry.reason ? entry.reason.slice(0, 300) : null,
        entry.rowCount ?? null,
        Math.round(entry.durationMs),
      ],
    );
  } finally {
    await conn.end().catch(() => undefined);
  }
}

/** Open a raw connection as the MCP user. Used only by the tests, to prove what the database itself refuses. */
export async function openRawForTests(): Promise<Connection> {
  return mysql.createConnection(connectionOptions());
}
