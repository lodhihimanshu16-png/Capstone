// Negative tests against a real MySQL. Each one proves something is REFUSED.
// They need the database set up with db-setup/02-mcp-views.sql and 03-create-mcp-user.sql and
//   HARBOURLINE_MCP_DSN   the MCP user's connection string (required; the suite is skipped without it)
//   MCP_TEST_ADMIN_DSN    optional, an admin connection string, only used to read the audit log
// The suite deliberately bypasses the guard in some tests to show the DATABASE refuses too.
import type { Connection } from 'mysql2/promise';
import mysql from 'mysql2/promise';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

const HAVE_DB = Boolean(process.env.HARBOURLINE_MCP_DSN);
if (!HAVE_DB) {
  // CI sets MCP_REQUIRE_DB=1 so that a missing database fails the job instead of skipping the tests.
  if (process.env.MCP_REQUIRE_DB) throw new Error('MCP_REQUIRE_DB is set but HARBOURLINE_MCP_DSN is not.');
  console.warn('HARBOURLINE_MCP_DSN is not set: the MySQL negative tests are SKIPPED.');
}

process.env.MCP_MAX_ROWS = '5';
process.env.MCP_MAX_EXECUTION_MS = '500';

type Tools = typeof import('../src/tools.js');
type Db = typeof import('../src/db.js');
let tools: Tools;
let db: Db;
let raw: Connection; // the MCP user, with NO guard in front of it

/** Call the server's run_readonly_query exactly as a client would. */
async function ask(sql: string) {
  return tools.callTool(tools.runReadonlyQuery as never, { sql });
}
async function rejection(promise: Promise<unknown>) {
  try {
    await promise;
  } catch (e) {
    return e as { code?: string; errno?: number; message: string };
  }
  throw new Error('Expected the database to refuse this, but it succeeded.');
}

describe.skipIf(!HAVE_DB)('MCP negative tests (MySQL)', () => {
  beforeAll(async () => {
    tools = await import('../src/tools.js');
    db = await import('../src/db.js');
    raw = await db.openRawForTests();
  });
  afterAll(async () => {
    await raw?.end();
  });

  describe('a write is refused', () => {
    it('by the server guard', async () => {
      const r = await ask("UPDATE mcp_claims SET status = 'Closed'");
      expect(r.ok).toBe(false);
    });
    it.each([
      ['INSERT', "INSERT INTO claims (claim_number) VALUES ('X')"],
      ['UPDATE', "UPDATE claims SET status = 'Closed'"],
      ['DELETE', 'DELETE FROM claim_status_history'],
      ['UPDATE through a view', "UPDATE mcp_claims SET status = 'Closed'"],
    ])('by the database itself (%s, guard bypassed)', async (_n, sql) => {
      const e = await rejection(raw.query(sql));
      expect(e.errno).toBeGreaterThan(0);
      expect(['ER_TABLEACCESS_DENIED_ERROR', 'ER_NONUPDATEABLE_COLUMN', 'ER_VIEW_NONUPD_CHECK']).toContain(e.code);
    });
  });

  describe('DDL is refused', () => {
    it('by the server guard', async () => {
      expect((await ask('DROP TABLE claims')).ok).toBe(false);
      expect((await ask('CREATE TABLE t (id INT)')).ok).toBe(false);
    });
    it.each(['DROP VIEW mcp_claims', 'CREATE TABLE x (id INT)', 'ALTER TABLE claims ADD x INT', 'TRUNCATE TABLE claims'])(
      'by the database itself (%s, guard bypassed)',
      async (sql) => {
        const e = await rejection(raw.query(sql));
        expect(e.code).toMatch(/ACCESS_DENIED|TABLEACCESS_DENIED|DBACCESS_DENIED/);
      },
    );
  });

  describe('multiple statements are refused', () => {
    it('by the server guard', async () => {
      expect((await ask('SELECT 1; SELECT 2')).ok).toBe(false);
      expect((await ask('SELECT 1; DROP TABLE claims')).ok).toBe(false);
    });
    it('by the database driver (multipleStatements is off)', async () => {
      const e = await rejection(raw.query('SELECT 1; SELECT 2'));
      expect(e.code).toBe('ER_PARSE_ERROR');
    });
  });

  describe('personal data is not reachable', () => {
    it.each(['full_name', 'email', 'phone', 'address_line', 'date_of_birth', 'postcode'])(
      'column %s does not exist in mcp_policyholders',
      async (col) => {
        const e = await rejection(raw.query(`SELECT ${col} FROM mcp_policyholders`));
        expect(e.code).toBe('ER_BAD_FIELD_ERROR');
      },
    );
    it('the view returns masked values only', async () => {
      const r = await ask('SELECT full_name_masked, email_masked, phone_masked, postcode_area FROM mcp_policyholders');
      expect(r.ok).toBe(true);
      if (!r.ok) return;
      expect(r.result.rows.length).toBeGreaterThan(0);
      for (const row of r.result.rows) {
        expect(String(row.full_name_masked)).toContain('***');
        expect(String(row.email_masked)).toMatch(/^.\*\*\*@/);
        if (row.phone_masked !== null) expect(String(row.phone_masked)).toMatch(/^\*+\d{3}$/);
        expect(String(row.postcode_area)).not.toContain(' ');
      }
    });
    it('the base table policyholders is refused', async () => {
      const e = await rejection(raw.query('SELECT full_name FROM policyholders'));
      expect(e.code).toBe('ER_TABLEACCESS_DENIED_ERROR');
    });
    it('the users and credentials table is refused', async () => {
      const e = await rejection(raw.query('SELECT email, password_hash FROM users'));
      expect(e.code).toBe('ER_TABLEACCESS_DENIED_ERROR');
      expect((await ask('SELECT * FROM users')).ok).toBe(false);
    });
  });

  describe('an out-of-scope region is not reachable', () => {
    it('every view returns the Midlands only', async () => {
      for (const view of ['mcp_claims', 'mcp_policies', 'mcp_policyholders', 'mcp_handlers']) {
        const [rows] = await raw.query(`SELECT DISTINCT region_code FROM ${view}`);
        expect((rows as { region_code: string }[]).map((r) => r.region_code)).toEqual(['MID']);
      }
    });
    it('asking for another region returns nothing', async () => {
      for (const region of ['NTH', 'LSE', 'SWW']) {
        const r = await ask(`SELECT * FROM mcp_claims WHERE region_code = '${region}'`);
        expect(r.ok).toBe(true);
        if (r.ok) expect(r.result.rows).toHaveLength(0);
      }
    });
    it('the base claims table, where other regions live, is refused', async () => {
      const e = await rejection(raw.query('SELECT * FROM claims WHERE region_id <> 2'));
      expect(e.code).toBe('ER_TABLEACCESS_DENIED_ERROR');
    });
    it('history only covers claims in scope', async () => {
      const [rows] = await raw.query(
        'SELECT COUNT(*) AS n FROM mcp_claim_history h WHERE h.claim_id NOT IN (SELECT claim_id FROM mcp_claims)',
      );
      expect((rows as { n: number }[])[0].n).toBe(0);
    });
  });

  describe('INTO OUTFILE is refused', () => {
    it('by the server guard', async () => {
      expect((await ask("SELECT * FROM mcp_claims INTO OUTFILE '/tmp/claims.txt'")).ok).toBe(false);
      expect((await ask("SELECT * FROM mcp_claims INTO DUMPFILE '/tmp/claims.bin'")).ok).toBe(false);
    });
    it('by the database itself (no FILE privilege, guard bypassed)', async () => {
      const e = await rejection(raw.query("SELECT * FROM mcp_claims INTO OUTFILE 'harbourline-should-not-exist.txt'"));
      expect(e.code).toBe('ER_SPECIFIC_ACCESS_DENIED_ERROR');
      expect(e.message).toContain('FILE');
    });
  });

  describe('the audit log', () => {
    it('can be written to by the MCP user', async () => {
      await raw.query("INSERT INTO mcp_audit_log (tool_name, outcome) VALUES ('negative-test', 'ok')");
    });
    it.each([
      ['read', 'SELECT * FROM mcp_audit_log'],
      ['changed', "UPDATE mcp_audit_log SET outcome = 'ok'"],
      ['deleted from', 'DELETE FROM mcp_audit_log'],
    ])('cannot be %s by the MCP user', async (_n, sql) => {
      const e = await rejection(raw.query(sql));
      expect(e.code).toBe('ER_TABLEACCESS_DENIED_ERROR');
    });
    it.skipIf(!process.env.MCP_TEST_ADMIN_DSN)('records every call, including refused ones', async () => {
      const admin = await mysql.createConnection(process.env.MCP_TEST_ADMIN_DSN!);
      try {
        const count = async () => ((await admin.query('SELECT COUNT(*) AS n FROM mcp_audit_log'))[0] as { n: number }[])[0].n;
        const before = await count();
        await ask('SELECT COUNT(*) AS n FROM mcp_claims');
        await ask('DROP TABLE claims');
        expect(await count()).toBe(before + 2);
        const [rows] = await admin.query('SELECT outcome FROM mcp_audit_log ORDER BY id DESC LIMIT 2');
        expect((rows as { outcome: string }[]).map((r) => r.outcome)).toEqual(['refused', 'ok']);
      } finally {
        await admin.end();
      }
    });
  });

  describe('the row cap and the timeout', () => {
    it('limit the rows returned (cap is 5 in this suite)', async () => {
      const r = await ask('SELECT claim_number FROM mcp_claims LIMIT 100000');
      expect(r.ok).toBe(true);
      if (r.ok) {
        expect(r.result.rows).toHaveLength(5);
        expect(r.result.truncated).toBe(true);
      }
    });
    it('stop a runaway query (limit is 500 ms in this suite)', async () => {
      const started = Date.now();
      const r = await ask(
        'SELECT COUNT(*) AS n FROM mcp_claim_history a, mcp_claim_history b, mcp_claim_history c, mcp_claim_history d',
      );
      expect(r.ok).toBe(false);
      expect(Date.now() - started).toBeLessThan(5000);
    });
    it('are set on the database session, not only in the server', async () => {
      // db.runSelect is the connection the server itself uses for every query.
      const r = await db.runSelect('SELECT @@session.max_execution_time AS ms, @@session.sql_select_limit AS rows_limit');
      expect(Number(r.rows[0].ms)).toBe(500);
      expect(Number(r.rows[0].rows_limit)).toBe(6); // cap 5, plus one row to detect truncation
    });
  });

  describe('the business tools answer from the views', () => {
    it.each(['claimsOverAuthority', 'ageingByStage', 'lossRatioByProduct', 'reopenedClaims'] as const)('%s runs', async (name) => {
      const r = await tools.callTool(tools[name] as never, {});
      expect(r.ok).toBe(true);
    });
  });
});
