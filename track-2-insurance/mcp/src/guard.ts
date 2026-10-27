// Guard for run_readonly_query. Accepts one read-only SELECT (or WITH ... SELECT) statement.
// It is a second line of defence: the database user has SELECT on views only, and that is what
// really limits access. The guard refuses by default and only allows what it can recognise.

export class GuardError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'GuardError';
  }
}

export const MAX_SQL_LENGTH = 4000;

// Words that must not appear anywhere outside a quoted string or a backtick identifier.
// A few are also harmless functions (REPLACE, INSERT, TRUNCATE). They are refused anyway, because
// a false refusal is cheap and a missed write is not.
const FORBIDDEN_WORDS = new Set([
  'INTO', 'OUTFILE', 'DUMPFILE',
  'INSERT', 'UPDATE', 'DELETE', 'REPLACE', 'TRUNCATE', 'MERGE',
  'CREATE', 'ALTER', 'DROP', 'RENAME',
  'GRANT', 'REVOKE',
  'SET', 'CALL', 'DO', 'HANDLER', 'LOAD', 'LOCK', 'UNLOCK', 'FLUSH', 'KILL', 'SHUTDOWN', 'RESET', 'PURGE',
  'PREPARE', 'EXECUTE', 'DEALLOCATE',
  'USE', 'SHOW', 'DESCRIBE', 'EXPLAIN', 'ANALYZE', 'OPTIMIZE', 'REPAIR', 'CHECKSUM',
  'BEGIN', 'START', 'COMMIT', 'ROLLBACK', 'SAVEPOINT',
  'FOR', // FOR UPDATE / FOR SHARE
  'SLEEP', 'BENCHMARK', 'LOAD_FILE', 'GET_LOCK', 'RELEASE_LOCK', 'RELEASE_ALL_LOCKS', 'IS_FREE_LOCK', 'IS_USED_LOCK',
  'MASTER_POS_WAIT', 'SOURCE_POS_WAIT', 'WAIT_FOR_EXECUTED_GTID_SET',
]);

const FORBIDDEN_SCHEMAS = ['information_schema', 'mysql', 'performance_schema', 'sys'];

export function guardReadonly(raw: unknown): string {
  if (typeof raw !== 'string') throw new GuardError('The query must be a string.');
  let sql = raw.trim();
  if (sql.length === 0) throw new GuardError('The query is empty.');
  if (sql.length > MAX_SQL_LENGTH) throw new GuardError(`The query is longer than ${MAX_SQL_LENGTH} characters.`);
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(sql)) throw new GuardError('The query contains control characters.');

  // One trailing semicolon is allowed. Any other semicolon outside a string is a second statement.
  if (sql.endsWith(';')) sql = sql.slice(0, -1).trimEnd();

  const words: string[] = [];
  const identifiers: string[] = [];
  let i = 0;
  while (i < sql.length) {
    const ch = sql[i];
    if (ch === "'" || ch === '"') {
      i = skipString(sql, i, ch);
      continue;
    }
    if (ch === '`') {
      const end = sql.indexOf('`', i + 1);
      if (end < 0) throw new GuardError('Unterminated backtick identifier.');
      identifiers.push(sql.slice(i + 1, end).toLowerCase());
      i = end + 1;
      continue;
    }
    if (ch === ';') throw new GuardError('Only a single statement is allowed (found a semicolon).');
    if (ch === '#') throw new GuardError('Comments are not allowed.');
    if (ch === '-' && sql[i + 1] === '-') throw new GuardError('Comments are not allowed.');
    if (ch === '/' && sql[i + 1] === '*') throw new GuardError('Comments are not allowed.');
    if (ch === '@') throw new GuardError('Variables are not allowed.');
    if (ch === '\\') throw new GuardError('Backslashes outside strings are not allowed.');
    if (/[A-Za-z_]/.test(ch)) {
      let j = i + 1;
      while (j < sql.length && /[A-Za-z0-9_$]/.test(sql[j])) j++;
      words.push(sql.slice(i, j).toUpperCase());
      i = j;
      continue;
    }
    i++;
  }

  if (words.length === 0) throw new GuardError('The query has no keywords.');
  if (words[0] !== 'SELECT' && words[0] !== 'WITH') {
    throw new GuardError(`Only SELECT queries are allowed (the query starts with ${words[0]}).`);
  }
  for (const word of words) {
    if (FORBIDDEN_WORDS.has(word)) throw new GuardError(`The keyword ${word} is not allowed.`);
  }
  for (const name of [...words.map((w) => w.toLowerCase()), ...identifiers]) {
    if (FORBIDDEN_SCHEMAS.includes(name)) throw new GuardError(`Access to the ${name} schema is not allowed.`);
  }
  return sql;
}

function skipString(sql: string, start: number, quote: string): number {
  let i = start + 1;
  while (i < sql.length) {
    const ch = sql[i];
    if (ch === '\\') {
      i += 2;
      continue;
    }
    if (ch === quote) {
      if (sql[i + 1] === quote) {
        i += 2;
        continue;
      }
      return i + 1;
    }
    i++;
  }
  throw new GuardError('Unterminated string.');
}
